using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Aidp.Api.Models;

namespace Aidp.Api.Services;

internal sealed record PlatformKnowledgeCatalog(
    IReadOnlyList<KnowledgeDocument> Documents, IReadOnlyList<KnowledgeChunk> Chunks,
    IReadOnlyList<KnowledgeConflict> Conflicts, KnowledgeCatalogStatus Status);

public interface IPlatformKnowledgeRetriever
{
    KnowledgeRetrievalResult Retrieve(KnowledgeQuery query);
}

internal static partial class PlatformKnowledgeLoader
{
    private const int MaximumDocumentBytes = 256 * 1024;
    private static readonly HashSet<string> AllowedMetadata = new(StringComparer.Ordinal)
    {
        "documentId", "title", "category", "version", "status", "environmentScopes", "workloadScopes",
        "lastUpdated", "owner", "authority", "tags", "reviewAfter", "supersedes"
    };
    private static readonly string[] RequiredMetadata =
    [
        "documentId", "title", "category", "version", "status", "environmentScopes", "workloadScopes",
        "lastUpdated", "owner", "authority", "tags", "reviewAfter"
    ];

    internal static PlatformKnowledgeCatalog Load(string root, string repositoryRevision, DateOnly today)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) throw new InvalidDataException("knowledgeDirectoryMissing");
        var documents = new List<KnowledgeDocument>();
        foreach (var path in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            documents.Add(LoadDocument(fullRoot, path, repositoryRevision));
        if (documents.GroupBy(x => x.DocumentId, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new InvalidDataException("duplicateDocumentId");
        if (documents.GroupBy(x => (x.DocumentId, x.Version)).Any(x => x.Count() > 1))
            throw new InvalidDataException("duplicateDocumentVersion");
        ValidateSupersession(documents);
        var active = documents.Where(x => x.Status == KnowledgeStatus.Active).ToList();
        var conflicts = DetectConflicts(active);
        var fresh = active.Where(x => x.ReviewAfter >= today).ToList();
        var chunks = fresh.SelectMany(MarkdownKnowledgeChunker.Chunk).ToList();
        if (chunks.GroupBy(x => x.ChunkId, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new InvalidDataException("duplicateChunkId");
        var stale = active.Count - fresh.Count;
        return new(documents, chunks, conflicts,
            new(true, documents.Count, active.Count, chunks.Count, stale, conflicts.Count, null));
    }

    internal static KnowledgeDocument LoadDocument(string root, string path, string revision)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal)) throw new InvalidDataException("knowledgePathTraversal");
        if (!string.Equals(Path.GetExtension(fullPath), ".md", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("unsupportedKnowledgeFileType");
        var info = new FileInfo(fullPath);
        if (!string.IsNullOrEmpty(info.LinkTarget) && info.ResolveLinkTarget(true) is { } target &&
            !target.FullName.StartsWith(fullRoot, StringComparison.Ordinal)) throw new InvalidDataException("knowledgeSymlinkEscape");
        if (info.Length > MaximumDocumentBytes) throw new InvalidDataException("knowledgeDocumentTooLarge");
        string text;
        try { text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(fullPath)); }
        catch (DecoderFallbackException) { throw new InvalidDataException("knowledgeDocumentNotUtf8"); }
        KnowledgeSafetyScanner.Validate(text);
        var (metadata, content) = ParseFrontMatter(text);
        var relative = Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
        var facts = ParseFacts(content);
        return new(
            Required(metadata, "documentId"), Required(metadata, "title"), ParseEnum<KnowledgeCategory>(metadata, "category"),
            ParseVersion(metadata), ParseEnum<KnowledgeStatus>(metadata, "status"), ParseList(metadata, "environmentScopes"),
            ParseList(metadata, "workloadScopes"), ParseDate(metadata, "lastUpdated"), Required(metadata, "owner"),
            ParseEnum<KnowledgeAuthority>(metadata, "authority"), ParseList(metadata, "tags"), ParseDate(metadata, "reviewAfter"),
            metadata.GetValueOrDefault("supersedes") is { Length: > 0 } supersedes ? supersedes : null,
            $"docs/knowledge/{relative}", revision, Hash(text), facts, content);
    }

    private static (Dictionary<string, string> Metadata, string Content) ParseFrontMatter(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal)) throw new InvalidDataException("knowledgeFrontMatterMissing");
        var end = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("knowledgeFrontMatterMalformed");
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in normalized[4..end].Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator < 1) throw new InvalidDataException("knowledgeFrontMatterMalformed");
            var key = line[..separator].Trim();
            if (!AllowedMetadata.Contains(key)) throw new InvalidDataException("knowledgeMetadataUnknown");
            if (!metadata.TryAdd(key, line[(separator + 1)..].Trim().Trim('"'))) throw new InvalidDataException("knowledgeMetadataDuplicate");
        }
        if (RequiredMetadata.Any(key => !metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
            throw new InvalidDataException("knowledgeMetadataRequired");
        return (metadata, normalized[(end + 5)..].Trim());
    }

    private static IReadOnlyList<TrustedFact> ParseFacts(string content)
    {
        var facts = new List<TrustedFact>();
        foreach (Match match in FactRegex().Matches(content))
        {
            if (!Enum.TryParse<KnowledgeFactKind>(match.Groups[2].Value, true, out var kind))
                throw new InvalidDataException("knowledgeFactKindInvalid");
            facts.Add(new(match.Groups[1].Value, kind, match.Groups[3].Value.Trim()));
        }
        if (facts.GroupBy(x => x.FactId, StringComparer.Ordinal).Any(x => x.Count() > 1))
            throw new InvalidDataException("duplicateFactIdInDocument");
        return facts;
    }

    private static IReadOnlyList<KnowledgeConflict> DetectConflicts(IReadOnlyList<KnowledgeDocument> documents) => documents
        .SelectMany(document => document.TrustedFacts.Select(fact => (document, fact)))
        .GroupBy(x => x.fact.FactId, StringComparer.Ordinal)
        .Where(group => group.Select(x => x.fact.Statement).Distinct(StringComparer.Ordinal).Count() > 1)
        .Select(group => new KnowledgeConflict(group.Key, group.Select(x => Reference(x.document)).ToList()))
        .OrderBy(x => x.FactId, StringComparer.Ordinal).ToList();

    private static void ValidateSupersession(IReadOnlyList<KnowledgeDocument> documents)
    {
        foreach (var document in documents.Where(x => x.Supersedes is not null))
        {
            var prior = documents.SingleOrDefault(x => x.DocumentId == document.Supersedes);
            if (prior is null || prior.Status is not (KnowledgeStatus.Deprecated or KnowledgeStatus.Retired))
                throw new InvalidDataException("knowledgeSupersessionInvalid");
        }
    }

    internal static KnowledgeDocumentReference Reference(KnowledgeDocument document) =>
        new(document.DocumentId, document.Version, document.Title, document.SourcePath, document.LastUpdated, document.ReviewAfter);
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Required(Dictionary<string, string> metadata, string key) => metadata[key].Trim();
    private static T ParseEnum<T>(Dictionary<string, string> metadata, string key) where T : struct, Enum =>
        Enum.TryParse<T>(metadata[key], true, out var value) && Enum.IsDefined(value) ? value : throw new InvalidDataException($"knowledge{key}Invalid");
    private static string ParseVersion(Dictionary<string, string> metadata) =>
        VersionRegex().IsMatch(metadata["version"]) ? metadata["version"] : throw new InvalidDataException("knowledgeVersionInvalid");
    private static DateOnly ParseDate(Dictionary<string, string> metadata, string key) =>
        DateOnly.TryParseExact(metadata[key], "yyyy-MM-dd", out var value) ? value : throw new InvalidDataException($"knowledge{key}Invalid");
    private static IReadOnlyList<string> ParseList(Dictionary<string, string> metadata, string key)
    {
        var value = metadata[key];
        if (!value.StartsWith('[') || !value.EndsWith(']')) throw new InvalidDataException($"knowledge{key}Invalid");
        var result = value[1..^1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim('"', '\'')).Where(x => x.Length > 0).ToList();
        return result.Count > 0 ? result : throw new InvalidDataException($"knowledge{key}Invalid");
    }

    [GeneratedRegex(@"(?m)^- Fact: ([a-z0-9][a-z0-9.-]*) \| ([a-zA-Z]+) \| (.+)$")]
    private static partial Regex FactRegex();
    [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex VersionRegex();
}

internal static partial class KnowledgeSafetyScanner
{
    internal static void Validate(string text)
    {
        if (EmbeddedMarkup().IsMatch(text)) throw new InvalidDataException("knowledgeEmbeddedMarkup");
        if (PromptInjection().IsMatch(text)) throw new InvalidDataException("knowledgePromptInjection");
        if (Credential().IsMatch(text) || Jwt().IsMatch(text) || Sas().IsMatch(text) || PrivateKey().IsMatch(text))
            throw new InvalidDataException("knowledgeCredentialDetected");
    }

    internal static KnowledgeCommandSafety ClassifyCommand(string command)
    {
        if (DestructiveCommand().IsMatch(command)) return KnowledgeCommandSafety.Destructive;
        if (StateChangingCommand().IsMatch(command)) return KnowledgeCommandSafety.StateChanging;
        return KnowledgeCommandSafety.ReadOnly;
    }

    [GeneratedRegex(@"(?is)<\s*(script|iframe|object|embed|form|style|html)\b|data\s*:")]
    private static partial Regex EmbeddedMarkup();
    [GeneratedRegex(@"(?i)\b(ignore (all |any )?(previous|prior|system|developer) instructions|system prompt|reveal secrets?|disregard (the )?(policy|instructions)|act as (the )?system|override (the )?instructions)\b")]
    private static partial Regex PromptInjection();
    [GeneratedRegex(@"(?i)(authorization\s*:|bearer\s+[a-z0-9._~-]+|password\s*[:=]|client[_ -]?secret\s*[:=]|accountkey\s*=|connectionstring\s*[:=]|pat\s*[:=])")]
    private static partial Regex Credential();
    [GeneratedRegex(@"\beyJ[a-zA-Z0-9_-]{8,}\.[a-zA-Z0-9_-]{8,}\.[a-zA-Z0-9_-]{8,}\b")]
    private static partial Regex Jwt();
    [GeneratedRegex(@"(?i)[?&](sig|se|sp|sv|spr|srt|ss)=[^\s&#]+")]
    private static partial Regex Sas();
    [GeneratedRegex(@"-----BEGIN (RSA |EC |OPENSSH )?(PRIVATE KEY|CERTIFICATE)-----")]
    private static partial Regex PrivateKey();
    [GeneratedRegex(@"(?i)\b(rm\s+-rf|terraform\s+destroy|az\s+group\s+delete|drop\s+(database|table))\b")]
    private static partial Regex DestructiveCommand();
    [GeneratedRegex(@"(?i)\b(terraform\s+(apply|import)|az\s+[^\n]*(create|update|delete|set)|kubectl\s+(apply|delete|patch)|git\s+push)\b")]
    private static partial Regex StateChangingCommand();
}

internal static partial class MarkdownKnowledgeChunker
{
    private const int TargetMaximum = 4000;
    private const int HardMaximum = 6000;

    internal static IReadOnlyList<KnowledgeChunk> Chunk(KnowledgeDocument document)
    {
        var sections = Sections(RemoveUnsafeCommandBlocks(document.Content), document.Title);
        var chunks = new List<KnowledgeChunk>();
        foreach (var section in sections)
        {
            var ordinal = 1;
            foreach (var content in SplitBlocks(section.Content))
            {
                var normalized = content.Trim();
                if (normalized.Length == 0) continue;
                var id = $"knowledge:{document.DocumentId}@{document.Version}#{Slug(section.Path)}:{ordinal++:00}";
                chunks.Add(new(id, document.DocumentId, document.Version, document.Title, document.Category,
                    document.Authority, section.Path, document.EnvironmentScopes, document.WorkloadScopes,
                    document.Tags, document.LastUpdated, document.ReviewAfter, document.SourcePath, document.RepositoryRevision,
                    PlatformKnowledgeLoader.Hash(document.DocumentId + document.Version + section.Path + normalized),
                    normalized, document.TrustedFacts.Where(f => normalized.Contains(f.FactId, StringComparison.Ordinal)).ToList()));
            }
        }
        return chunks;
    }

    internal static string RemoveUnsafeCommandBlocks(string content)
    {
        return FenceRegex().Replace(content, match =>
            KnowledgeSafetyScanner.ClassifyCommand(match.Groups[2].Value) == KnowledgeCommandSafety.ReadOnly ? match.Value : string.Empty);
    }

    private static IReadOnlyList<(string Path, string Content)> Sections(string markdown, string title)
    {
        var result = new List<(string, string)>();
        var headings = new string[6];
        var path = title;
        var buffer = new StringBuilder();
        void Flush() { if (buffer.Length > 0) { result.Add((path, buffer.ToString())); buffer.Clear(); } }
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var match = HeadingRegex().Match(line);
            if (match.Success)
            {
                Flush();
                var level = match.Groups[1].Value.Length;
                headings[level - 1] = match.Groups[2].Value.Trim();
                for (var i = level; i < headings.Length; i++) headings[i] = string.Empty;
                path = string.Join(" / ", headings.Where(x => !string.IsNullOrEmpty(x)));
            }
            buffer.AppendLine(line);
        }
        Flush();
        return result;
    }

    private static IEnumerable<string> SplitBlocks(string content)
    {
        var protectedContent = FenceRegex().Replace(content, match => match.Value.Replace("\n\n", "\n\0\n", StringComparison.Ordinal));
        var blocks = BlockRegex().Split(protectedContent).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Replace("\n\0\n", "\n\n", StringComparison.Ordinal)).ToList();
        var current = new StringBuilder();
        foreach (var block in blocks)
        {
            if (block.Length > HardMaximum) throw new InvalidDataException("knowledgeBlockTooLarge");
            if (current.Length >= 2000 && current.Length + block.Length + 2 > TargetMaximum ||
                current.Length > 0 && current.Length + block.Length + 2 > HardMaximum)
            {
                yield return current.ToString(); current.Clear();
            }
            if (current.Length > 0) current.AppendLine().AppendLine();
            current.Append(block.Trim());
        }
        if (current.Length > 0) yield return current.ToString();
    }

    private static string Slug(string value)
    {
        var slug = NonSlug().Replace(value.ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "root" : slug;
    }

    [GeneratedRegex(@"(?m)^(#{1,6})\s+(.+)$")]
    private static partial Regex HeadingRegex();
    [GeneratedRegex(@"(?ms)(```[^\n]*\n)(.*?)(^```\s*$)")]
    private static partial Regex FenceRegex();
    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlockRegex();
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlug();
}

internal sealed class PlatformKnowledgeRetriever(PlatformKnowledgeCatalog catalog) : IPlatformKnowledgeRetriever
{
    private const int MaximumContextCharacters = 18000;

    public KnowledgeRetrievalResult Retrieve(KnowledgeQuery query)
    {
        if (!catalog.Status.Available) return new(KnowledgeRetrievalStatus.Unavailable, [], [], []);
        if (catalog.Conflicts.Count > 0) return new(KnowledgeRetrievalStatus.KnowledgeConflict, [], catalog.Conflicts, []);
        var stale = catalog.Documents.Where(x => x.Status == KnowledgeStatus.Active && x.ReviewAfter < DateOnly.FromDateTime(DateTime.UtcNow) &&
                Scope(x.EnvironmentScopes, query.Environment) && Scope(x.WorkloadScopes, query.WorkloadType) &&
                (query.Categories.Count == 0 || query.Categories.Contains(x.Category)) && DocumentScore(x, query.ControlledTerms) > 0)
            .Select(PlatformKnowledgeLoader.Reference).ToList();
        var max = Math.Clamp(query.MaximumChunks, 3, 6);
        var terms = query.ControlledTerms.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var candidates = catalog.Chunks.Where(x => x.ReviewAfter >= today && Scope(x.EnvironmentScopes, query.Environment) &&
            Scope(x.WorkloadScopes, query.WorkloadType) && (query.Categories.Count == 0 || query.Categories.Contains(x.Category)))
            .Select(chunk => (chunk, score: Score(chunk, terms, query.Environment, query.WorkloadType)))
            .Where(x => x.score > 0).OrderByDescending(x => x.score).ThenBy(x => x.chunk.ChunkId, StringComparer.Ordinal);
        var selected = new List<KnowledgeChunk>();
        var size = 0;
        foreach (var candidate in candidates)
        {
            if (selected.Count == max || size + candidate.chunk.Content.Length > MaximumContextCharacters) break;
            selected.Add(candidate.chunk); size += candidate.chunk.Content.Length;
        }
        if (selected.Count > 0) return new(KnowledgeRetrievalStatus.Success, selected, [], stale);
        return new(stale.Count > 0 ? KnowledgeRetrievalStatus.StaleKnowledge : KnowledgeRetrievalStatus.NoRelevantKnowledge, [], [], stale);
    }

    private static bool Scope(IReadOnlyList<string> scopes, string? value) => scopes.Contains("all", StringComparer.OrdinalIgnoreCase) ||
        value is not null && scopes.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static int Score(KnowledgeChunk chunk, IReadOnlyList<string> terms, string environment, string? workload)
    {
        var score = 0;
        foreach (var term in terms)
        {
            if (chunk.TrustedFacts.Any(x => Contains(x.Statement, term) || Contains(x.FactId, term))) score += 12;
            if (Contains(chunk.Title, term)) score += 10;
            if (Contains(chunk.SectionPath, term)) score += 8;
            if (chunk.Tags.Any(tag => Contains(tag, term))) score += 7;
            if (Contains(chunk.Category.ToString(), term)) score += 6;
            if (chunk.Content.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 2;
        }
        if (score > 0 && chunk.EnvironmentScopes.Contains(environment, StringComparer.OrdinalIgnoreCase)) score += 3;
        if (score > 0 && workload is not null && chunk.WorkloadScopes.Contains(workload, StringComparer.OrdinalIgnoreCase)) score += 3;
        return score;
    }
    private static int DocumentScore(KnowledgeDocument document, IReadOnlyList<string> terms) => terms.Sum(term =>
        (Contains(document.Title, term) ? 10 : 0) +
        (document.Tags.Any(tag => Contains(tag, term)) ? 7 : 0) +
        (document.TrustedFacts.Any(fact => Contains(fact.Statement, term) || Contains(fact.FactId, term)) ? 12 : 0) +
        (document.Content.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2 : 0));
    private static bool Contains(string value, string term) => value.Contains(term, StringComparison.OrdinalIgnoreCase);
}

internal static class KnowledgeCitationValidator
{
    internal static string? Validate(KnowledgeCitation citation, KnowledgeRetrievalResult retrieval)
    {
        if (citation.Type == KnowledgeCitationType.ModelInference)
            return citation.SourceReference is null ? null : "invalidModelInferenceCitation";
        if (citation.SourceReference is null) return "invalidKnowledgeCitation";
        return retrieval.Chunks.Any(x => x.ChunkId == citation.SourceReference) ? null : "invalidKnowledgeCitation";
    }
}
