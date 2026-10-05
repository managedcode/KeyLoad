using KeyLoad;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Original environment identities captured only at the composition boundary.</summary>
[ConfigurationOptions]
internal sealed record BenchmarkProvenanceOptions
{
    public string SourceRevision { get; set; } = string.Empty;
    public string WorkflowRunId { get; set; } = string.Empty;
    public string RunAttempt { get; set; } = string.Empty;
    public string JobId { get; set; } = string.Empty;
    public string? DockerHost { get; set; }
    public string? DockerContext { get; set; }
}
