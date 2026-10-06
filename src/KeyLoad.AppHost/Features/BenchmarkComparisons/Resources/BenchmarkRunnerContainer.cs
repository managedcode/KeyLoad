using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Hosting;

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
    private const string MissingSource = "The actual GitHub source revision is required for the container runner.";

    internal static IResourceBuilder<ContainerResource> Create(IDistributedApplicationBuilder builder, string hostOutput)
    {
        var runnerImage = RuntimeContainerImage.Read(builder, RunnerConfiguration);
        var serverImage = RuntimeContainerImage.Read(builder, RuntimeContainerImage.ServerConfiguration);
        var source = AppHostOptionsRegistration.Get(builder).Provenance.Value.SourceRevision ?? throw new InvalidOperationException(MissingSource);
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
        BenchmarkProvenanceRegistration.Apply(runner, AppHostOptionsRegistration.Get(builder).Provenance);
        return runner;
    }
}
