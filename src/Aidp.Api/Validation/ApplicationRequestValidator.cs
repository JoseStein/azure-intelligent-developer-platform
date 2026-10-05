using System.Text.RegularExpressions;
using Aidp.Api.Models;

namespace Aidp.Api.Validation;

public static class ApplicationRequestValidator
{
    private static readonly Regex ApplicationNamePattern = new(
        @"\A[a-z0-9][a-z0-9-]*[a-z0-9]\z",
        RegexOptions.CultureInvariant);

    public static Dictionary<string, string[]> Validate(CreateApplicationRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.ResourceType))
        {
            errors[nameof(request.ResourceType)] = ["ResourceType is required."];
        }
        else if (!string.Equals(request.ResourceType, "appservice", StringComparison.Ordinal))
        {
            errors[nameof(request.ResourceType)] = ["ResourceType must be appservice."];
        }

        if (string.IsNullOrWhiteSpace(request.ApplicationName))
        {
            errors[nameof(request.ApplicationName)] = ["ApplicationName is required."];
        }
        else if (request.ApplicationName.Length is < 3 or > 30 ||
                 !ApplicationNamePattern.IsMatch(request.ApplicationName))
        {
            errors[nameof(request.ApplicationName)] =
                ["ApplicationName must be 3–30 characters, use only lowercase letters, numbers, and hyphens, and cannot start or end with a hyphen."];
        }

        if (string.Equals(request.ApplicationName, "aidp", StringComparison.Ordinal))
        {
            errors[nameof(request.ApplicationName)] = ["ApplicationName aidp is reserved for the platform."];
        }

        if (string.IsNullOrWhiteSpace(request.Runtime))
        {
            errors[nameof(request.Runtime)] = ["Runtime is required."];
        }
        else if (!string.Equals(request.Runtime, "dotnet10", StringComparison.Ordinal))
        {
            errors[nameof(request.Runtime)] = ["Runtime must be dotnet10."];
        }

        if (string.IsNullOrWhiteSpace(request.Environment))
        {
            errors[nameof(request.Environment)] = ["Environment is required."];
        }
        else if (!string.Equals(request.Environment, "dev", StringComparison.Ordinal))
        {
            errors[nameof(request.Environment)] = ["Environment must be dev."];
        }

        if (request.Description?.Length > 200)
        {
            errors[nameof(request.Description)] = ["Description must be 200 characters or fewer."];
        }

        return errors;
    }
}
