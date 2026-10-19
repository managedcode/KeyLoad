using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStoragePartialClose
{
    private const int NoFailures = 0;
    internal const int HostCloseIndex = 0;
    internal const int LowerCloseIndex = 1;
    internal const int OwnerCloseIndex = 2;
    private const int OriginalCloseCount = 3;

    internal static async Task RunAsync(WebApplication application, OrleansNode? node,
        PartitionHostApplicationOwner? owner, PartitionStorageLifecycle? lower, bool refusalProved,
        List<Exception> failures)
    {
        if (node is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => node.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        }
        var originalFaults = new List<Exception>();
        var originals = new Task?[OriginalCloseCount];
        await ServerFailureObserver.ObserveAsync(() =>
        {
            var original = application.StopAsync(CancellationToken.None);
            originals[HostCloseIndex] = original;
            return original;
        }, originalFaults).ConfigureAwait(false);
        if (lower is not null)
        {
            await ServerFailureObserver.ObserveAsync(() =>
            {
                var original = lower.StopAsync(CancellationToken.None);
                originals[LowerCloseIndex] = original;
                return original;
            }, originalFaults).ConfigureAwait(false);
        }
        if (owner?.Closing is { } closing)
        {
            originals[OwnerCloseIndex] = closing;
            await ServerFailureObserver.ObserveAsync(() => closing, originalFaults).ConfigureAwait(false);
        }
        var proof = new List<Exception>();
        if (refusalProved)
        {
            await ServerFailureObserver.ObserveAsync(() => PartitionStorageStopAssertions.RequireAsync(
                node, owner, lower, originals, originalFaults), proof).ConfigureAwait(false);
        }
        var disposal = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => application.DisposeAsync().AsTask(), disposal).ConfigureAwait(false);
        if (!refusalProved || proof.Count != NoFailures || disposal.Count != NoFailures)
        {
            failures.AddRange(originalFaults);
            failures.AddRange(proof);
            failures.AddRange(disposal);
        }
    }
}
