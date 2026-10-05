namespace Aidp.Api.Services;

public sealed class AiReviewOptions
{
    public const string SectionName = "AiReview";
    public string Provider { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ModelDeployment { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class AiReviewException(
    string errorCode,
    int statusCode,
    string? failureStage = null,
    string? providerStatus = null,
    string? semanticRule = null) : Exception
{
    public string ErrorCode { get; } = errorCode;
    public int StatusCode { get; } = statusCode;
    public string? FailureStage { get; } = failureStage;
    public string? ProviderStatus { get; } = providerStatus;
    public string? SemanticRule { get; } = semanticRule;
}
