using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

internal static class TimeSeriesComparisonApplication
{
    internal static async Task<int> RunAsync(IConfiguration configuration, CancellationToken cancellationToken, TimeProvider? provider = null)
    {
        var settings = TimeSeriesHostSettings.Read(configuration).Value;
        var timeProvider = provider ?? TimeProvider.System;

        await using var owner = new TimeSeriesComparisonTargetOwner(NativeComparisonExecutionRegistration.ReadClient(configuration),
            NativeComparisonExecutionRegistration.Read(configuration),
            NativeComparisonExecutionRegistration.ReadLifecycle(configuration), timeProvider);
        var targets = owner.CreateTargets(settings.Endpoint, settings.AdminKey, settings.ConnectionString,
            settings.Image, settings.KeyLoadBuildIdentity);
        return await TimeSeriesComparisonRunner.RunAsync(targets, settings.SourceRevision, settings.OutputDirectory,
            NativeComparisonExecutionRegistration.Read(configuration), cancellationToken: cancellationToken,
            provenance: settings.Identity?.Provenance, loadGeneratorImage: settings.Identity?.LoadGeneratorImage, timeProvider: timeProvider);
    }

}
