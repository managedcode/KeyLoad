using KeyLoad;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

/// <summary>Preserves original environment provenance without accepting caller substitutions.</summary>
[ConfigurationBinding]
internal static class BenchmarkProvenanceRegistration
{
    private const string Source = "GITHUB_SHA";
    private const string Run = "GITHUB_RUN_ID";
    private const string Attempt = "GITHUB_RUN_ATTEMPT";
    private const string Job = "KEYLOAD_COMPARISON_JOB_ID";
    private const string DockerHost = "DOCKER_HOST";
    private const string DockerContext = "DOCKER_CONTEXT";

    internal static IOptions<BenchmarkProvenanceOptions> Bind()
    {
        var factory = new OptionsFactory<BenchmarkProvenanceOptions>([new ConfigureOptions<BenchmarkProvenanceOptions>(options =>
        {
            var values = Read();
            options.SourceRevision = values.SourceRevision;
            options.WorkflowRunId = values.WorkflowRunId;
            options.RunAttempt = values.RunAttempt;
            options.JobId = values.JobId;
            options.DockerHost = values.DockerHost;
            options.DockerContext = values.DockerContext;
        })], [], []);
        IOptions<BenchmarkProvenanceOptions> options = new OptionsManager<BenchmarkProvenanceOptions>(factory);
        _ = options.Value;
        return options;
    }

    private static BenchmarkProvenanceOptions Read() => new()
    {
        SourceRevision = Environment.GetEnvironmentVariable(Source) ?? string.Empty,
        WorkflowRunId = Environment.GetEnvironmentVariable(Run) ?? string.Empty,
        RunAttempt = Environment.GetEnvironmentVariable(Attempt) ?? string.Empty,
        JobId = Environment.GetEnvironmentVariable(Job) ?? string.Empty,
        DockerHost = Environment.GetEnvironmentVariable(DockerHost),
        DockerContext = Environment.GetEnvironmentVariable(DockerContext)
    };
}
