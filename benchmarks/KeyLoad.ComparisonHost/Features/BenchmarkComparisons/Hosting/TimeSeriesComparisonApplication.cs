using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal static class TimeSeriesComparisonApplication
{
    private const string KeyLoadEndpointSetting = "Benchmarks:KeyLoadEndpoint";
    private const string AdminKeySetting = "Benchmarks:AdminKey";
    private const string TimescaleConnectionSetting = "ConnectionStrings:benchmark-timescale";
    private const string TimescaleImageSetting = "Benchmarks:Images:Timescale";
    private const string KeyLoadBuildSetting = "Benchmarks:Images:KeyLoad";
    private const string OutputSetting = "Benchmarks:Output";
    private const string SourceRevisionSetting = "Benchmarks:SourceRevision";
    private const string StorageSetting = "Benchmarks:Storage";
    private const string UnrecordedRevision = "unrecorded";

    internal static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var endpoint = new Uri(Required(configuration, KeyLoadEndpointSetting), UriKind.Absolute);
        var adminKey = Required(configuration, AdminKeySetting);
        var connectionString = Required(configuration, TimescaleConnectionSetting);
        var image = Required(configuration, TimescaleImageSetting);
        var keyLoadBuildIdentity = Required(configuration, KeyLoadBuildSetting);
        var output = Path.GetFullPath(Required(configuration, OutputSetting));
        var sourceRevision = configuration[SourceRevisionSetting] ?? UnrecordedRevision;
        _ = Required(configuration, StorageSetting);
        var executionIdentity = ComparisonExecutionIdentity.ReadTimeSeries(configuration, sourceRevision);

        await using var owner = new TimeSeriesComparisonTargetOwner(NativeComparisonExecutionRegistration.ReadClient(configuration),
            NativeComparisonExecutionRegistration.ReadLifecycle(configuration));
        var targets = owner.CreateTargets(endpoint, adminKey, connectionString, image, keyLoadBuildIdentity);
        return await TimeSeriesComparisonRunner.RunAsync(targets, sourceRevision, output, cancellationToken,
            executionIdentity?.Provenance, executionIdentity?.LoadGeneratorImage);
    }

    private static string Required(IConfiguration configuration, string key)
        => configuration[key] ?? throw new InvalidOperationException("Missing benchmark setting: " + key);
}
