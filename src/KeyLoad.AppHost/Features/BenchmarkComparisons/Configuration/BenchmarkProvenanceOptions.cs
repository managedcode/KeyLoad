
namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Original environment identities captured only at the composition boundary.</summary>
[ConfigurationOptions]
internal sealed record BenchmarkProvenanceOptions
{
    public string? SourceRevision { get; set; }
    public string? WorkflowRunId { get; set; }
    public string? RunAttempt { get; set; }
    public string? JobId { get; set; }
    public string? Repository { get; set; }
    public string? Reference { get; set; }
    public string? Workflow { get; set; }
    public string? Actions { get; set; }
    public string? ImageReceipt { get; set; }
    public string? DockerHost { get; set; }
    public string? DockerContext { get; set; }
}
