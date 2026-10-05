namespace Aidp.Api.AzureDevOps;

public sealed class AzureDevOpsOptions
{
    public string Organization { get; set; } = "";
    public string Project { get; set; } = "";
    public int WorkloadPipelineId { get; set; }
    public string WorkloadBranch { get; set; } = "";
    public bool QueueEnabled { get; set; } = false;
}
