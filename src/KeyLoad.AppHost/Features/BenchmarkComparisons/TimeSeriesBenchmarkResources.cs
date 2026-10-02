namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class TimeSeriesBenchmarkResources
{
    private const string TimescaleName = "benchmark-timescale-server";
    private const string DatabaseName = "benchmark-timescale";
    private const string RunnerName = "comparisons";
    private const string TimescaleImage = "timescale/timescaledb";
    private const string TimescaleImageTag = "2.30.2-pg18";
    private const string TimescaleDigest = "sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    private const int DigestPrefixLength = 7;
    private const string TimescaleImageSetting = "Benchmarks__Images__Timescale";
    private const string KeyLoadBuildSetting = "Benchmarks__Images__KeyLoad";
    private const string SourceRevisionSetting = "Benchmarks:SourceRevision";
    private const string OutputSetting = "Benchmarks:Output";
    private const string DefaultOutputDirectory = "reports/timeseries";
    private const string UnrecordedRevision = "unrecorded";

    internal static void Add(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource>[] nodes,
        IResourceBuilder<ParameterResource> admin, string benchmarkRoot)
    {
        var database = builder.AddPostgres(TimescaleName)
            .WithImage(TimescaleImage)
            .WithImageTag(TimescaleImageTag)
            .WithImageSHA256(TimescaleDigest[DigestPrefixLength..])
            .WithArgs("-c", "synchronous_commit=on")
            .AddDatabase(DatabaseName);
        var imageReference = "docker.io/" + TimescaleImage + ":" + TimescaleImageTag + "@" + TimescaleDigest;
        var sourceRevision = builder.Configuration[SourceRevisionSetting]
            ?? Environment.GetEnvironmentVariable("GITHUB_SHA") ?? UnrecordedRevision;
        var keyLoadBuildIdentity = "KeyLoad RF3 Dockerfile build at " + sourceRevision;
        var output = Path.GetFullPath(builder.Configuration[OutputSetting]
            ?? Path.Combine(benchmarkRoot, DefaultOutputDirectory));
        var runner = builder.AddProject<Projects.KeyLoad_ComparisonHost>(RunnerName, launchProfileName: null)
            .WithReference(database)
            .WaitFor(database)
            .WithEnvironment("Benchmarks__Profile", AppHostConfiguration.TimeSeriesBenchmarkProfile)
            .WithEnvironment("Benchmarks__KeyLoadEndpoint", nodes[0].GetEndpoint("http"))
            .WithEnvironment("Benchmarks__AdminKey", admin)
            .WithEnvironment("Benchmarks__Output", output)
            .WithEnvironment("Benchmarks__Storage", "TimescaleDB single-node container; no cross-run persistent volume")
            .WithEnvironment(TimescaleImageSetting, imageReference)
            .WithEnvironment(KeyLoadBuildSetting, keyLoadBuildIdentity)
            .WithEnvironment("Benchmarks__SourceRevision", sourceRevision);
        foreach (var node in nodes)
        {
            runner.WaitFor(node);
        }
    }
}
