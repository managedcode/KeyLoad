using KeyLoad.AppHost.Hosting;
namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class TimeSeriesBenchmarkResources
{
    private const string DigestSeparator = "@";
    private const string KeyLoadEndpointEnvironment = "Benchmarks__KeyLoadEndpoint";
    private const string HttpEndpointName = "http";
    private const string AdminKeyEnvironment = "Benchmarks__AdminKey";
    private const string StorageEnvironment = "Benchmarks__Storage";

    private const string TimescaleName = "benchmark-timescale-server";
    private const string DatabaseName = "benchmark-timescale";
    private const string TimescaleImage = "timescale/timescaledb";
    private const string TimescaleImageTag = "2.30.2-pg18";
    private const string TimescaleDigest = "sha256:e72689191e1c977892c53d6f2c344dbc4a9657a867dc8cc1899229f9d3672b2e";
    private const int DigestPrefixLength = 7;
    private const string TimescaleImageSetting = "Benchmarks__Images__Timescale";
    private const string DefaultOutputDirectory = "reports/timeseries";

    internal static void Add(IDistributedApplicationBuilder builder, IResourceBuilder<ContainerResource>[] nodes,
        IResourceBuilder<ParameterResource> admin, string benchmarkRoot)
    {
        const string ResultText = "-c";
        const string AddResultText = "synchronous_commit=on";
        const string ComparisonText = "docker.io/";
        const string AddComparisonText = ":";
        const string NameText = "Benchmarks__Profile";
        const string AddNameText = "Benchmarks__EvidenceProfile";
        const int FirstIndex = 0;
        const string ValueText = "TimescaleDB single-node container; no cross-run persistent volume";

        var database = builder.AddPostgres(TimescaleName)
            .WithImage(TimescaleImage)
            .WithImageTag(TimescaleImageTag)
            .WithImageSHA256(TimescaleDigest[DigestPrefixLength..])
            .WithArgs(ResultText, AddResultText)
            .AddDatabase(DatabaseName);
        var imageReference = ComparisonText + TimescaleImage + AddComparisonText + TimescaleImageTag + DigestSeparator + TimescaleDigest;
        var output = Path.GetFullPath(AppHostOptionsRegistration.Get(builder).Startup.Value.BenchmarkOutput
            ?? Path.Combine(benchmarkRoot, DefaultOutputDirectory));
        var runner = BenchmarkRunnerContainer.Create(builder, output)
            .WithReference(database)
            .WaitFor(database)
            .WithEnvironment(NameText, AppHostConfiguration.TimeSeriesBenchmarkProfile)
            .WithEnvironment(AddNameText, AppHostConfiguration.TimeSeriesBenchmarkProfile)
            .WithEnvironment(KeyLoadEndpointEnvironment, nodes[FirstIndex].GetEndpoint(HttpEndpointName))
            .WithEnvironment(AdminKeyEnvironment, admin)
            .WithEnvironment(StorageEnvironment, ValueText)
            .WithEnvironment(TimescaleImageSetting, imageReference);
        foreach (var node in nodes)
        {
            runner.WaitFor(node);
        }
    }
}
