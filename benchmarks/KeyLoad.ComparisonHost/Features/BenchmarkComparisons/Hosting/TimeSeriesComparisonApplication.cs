using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal static class TimeSeriesComparisonApplication
{
    internal static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken)
    {
        var settings = TimeSeriesHostSettings.Read(configuration).Value;

        await using var owner = new TimeSeriesComparisonTargetOwner(NativeComparisonExecutionRegistration.ReadClient(configuration),
            NativeComparisonExecutionRegistration.ReadLifecycle(configuration));
        var targets = owner.CreateTargets(settings.Endpoint, settings.AdminKey, settings.ConnectionString,
            settings.Image, settings.KeyLoadBuildIdentity);
        return await TimeSeriesComparisonRunner.RunAsync(targets, settings.SourceRevision, settings.OutputDirectory, cancellationToken,
            settings.Identity?.Provenance, settings.Identity?.LoadGeneratorImage);
    }

}
