namespace Aidp.Api.Models;

public sealed record CreateApplicationRequest(
    string ResourceType,
    string ApplicationName,
    string Runtime,
    string Environment,
    string? Description
);
