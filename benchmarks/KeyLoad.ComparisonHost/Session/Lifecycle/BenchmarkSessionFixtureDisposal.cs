using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Joins the original session operation and each actual execution lifetime before rethrowing their retained failures.</summary>
internal static class BenchmarkSessionFixtureDisposal
{
    internal static async ValueTask CompleteAsync(Task original, IAsyncDisposable? runtimeOwner, IDisposable? configuration)
    {
        ArgumentNullException.ThrowIfNull(original);
        var primary = (await RuntimeComparisonFailure.ObserveAsync(original)).Value;
        var cleanup = ImmutableArray.CreateBuilder<Exception>();
        if ((await RuntimeComparisonFailure.ObserveAsync(RuntimeDisposeAsync(runtimeOwner))).Value is { } runtimeFailure)
        {
            cleanup.Add(runtimeFailure);
        }
        if ((await RuntimeComparisonFailure.ObserveAsync(DisposeConfigurationAsync(configuration))).Value is { } configurationFailure)
        {
            cleanup.Add(configurationFailure);
        }
        if ((RuntimeComparisonFailure.Combine(primary, cleanup.ToImmutable())).Value is { } combined)
        {
            ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }

    internal static async Task RetainFailureAsync(Exception original, Task retention)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(retention);
        var retentionFailure = (await RuntimeComparisonFailure.ObserveAsync(retention)).Value;
        var combined = (RuntimeComparisonFailure.Combine(original,
            retentionFailure is null ? [] : [retentionFailure])).Value;
        ExceptionDispatchInfo.Capture(combined!).Throw();
    }

    private static async Task RuntimeDisposeAsync(IAsyncDisposable? runtimeOwner)
    {
        if (runtimeOwner is not null)
        {
            await runtimeOwner.DisposeAsync();
        }
    }

    private static async Task DisposeConfigurationAsync(IDisposable? configuration)
    {
        await Task.CompletedTask;
        configuration?.Dispose();
    }
}
