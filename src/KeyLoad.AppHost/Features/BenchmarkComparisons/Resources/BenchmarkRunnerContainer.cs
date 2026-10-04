using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class BenchmarkRunnerContainer
{
    private const string RunnerConfiguration = "Benchmarks:ContainerImages:LoadGenerator";
    private const string RunnerName = "comparisons";
    private const string ContainerReports = "/reports";
    private const string OutputEnvironment = "Benchmarks__Output";
    private const string RunnerImageEnvironment = "Benchmarks__LoadGeneratorImage";
    private const string ServerImageEnvironment = "Benchmarks__Images__KeyLoad";
    private const string SourceEnvironment = "Benchmarks__SourceRevision";
    private const string UserArgument = "--user";
    private const string SourceName = "GITHUB_SHA";
    private const string MissingSource = "The actual GitHub source revision is required for the container runner.";
    private static readonly string[] ProvenanceNames =
        [SourceName, "GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "GITHUB_REPOSITORY", "GITHUB_REF", "GITHUB_WORKFLOW", "KEYLOAD_COMPARISON_JOB_ID"];

    internal static IResourceBuilder<ContainerResource> Create(IDistributedApplicationBuilder builder, string hostOutput)
    {
        var runnerImage = RuntimeContainerImage.Read(builder, RunnerConfiguration);
        var serverImage = RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration);
        var source = Environment.GetEnvironmentVariable(SourceName) ?? throw new InvalidOperationException(MissingSource);
        var output = Path.GetFullPath(hostOutput);
        Directory.CreateDirectory(output);
        var runner = runnerImage.Add(builder, RunnerName)
            .WithBindMount(output, ContainerReports)
            .WithEnvironment(OutputEnvironment, ContainerReports)
            .WithEnvironment(RunnerImageEnvironment, runnerImage.Reference)
            .WithEnvironment(ServerImageEnvironment, serverImage.Reference)
            .WithEnvironment(SourceEnvironment, source);
        if (ClusterContainerUser.Resolve(builder) is { } user)
        {
            runner.WithContainerRuntimeArgs(UserArgument, user);
        }
        foreach (var name in ProvenanceNames)
        {
            if (Environment.GetEnvironmentVariable(name) is { } value)
            {
                runner.WithEnvironment(name, value);
            }
        }
        return runner;
    }
}
