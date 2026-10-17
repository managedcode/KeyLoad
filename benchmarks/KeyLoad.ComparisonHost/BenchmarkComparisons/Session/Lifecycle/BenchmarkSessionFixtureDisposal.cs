using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Joins the original session operation and each actual native lifetime before rethrowing their retained failures.</summary>
internal static class BenchmarkSessionFixtureDisposal
{
    internal static async ValueTask CompleteAsync(Task original, IAsyncDisposable? nativeOwner, IDisposable? configuration)
    {
        ArgumentNullException.ThrowIfNull(original);
        var primary = await NativeComparisonFailure.ObserveAsync(original);
        var cleanup = ImmutableArray.CreateBuilder<Exception>();
        if (await NativeComparisonFailure.ObserveAsync(DisposeNativeAsync(nativeOwner)) is { } nativeFailure)
        {
            cleanup.Add(nativeFailure);
        }
        if (await NativeComparisonFailure.ObserveAsync(DisposeConfigurationAsync(configuration)) is { } configurationFailure)
        {
            cleanup.Add(configurationFailure);
        }
        if (NativeComparisonFailure.Combine(primary, cleanup.ToImmutable()) is { } combined)
        {
            ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }

    internal static async Task RetainFailureAsync(Exception original, Task retention)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(retention);
        var retentionFailure = await NativeComparisonFailure.ObserveAsync(retention);
        var combined = NativeComparisonFailure.Combine(original,
            retentionFailure is null ? [] : [retentionFailure]);
        ExceptionDispatchInfo.Capture(combined!).Throw();
    }

    private static async Task DisposeNativeAsync(IAsyncDisposable? nativeOwner)
    {
        if (nativeOwner is not null)
        {
            await nativeOwner.DisposeAsync();
        }
    }

    private static async Task DisposeConfigurationAsync(IDisposable? configuration)
    {
        await Task.CompletedTask;
        configuration?.Dispose();
    }
}
