using KeyLoad.RecoveryHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativePartialClose
{
    private const int NoFailures = 0;
    internal const int HostCloseIndex = 0;
    internal const int LowerCloseIndex = 1;
    internal const int OwnerCloseIndex = 2;
    private const int OriginalCloseCount = 3;

    internal static async Task RunAsync(ControlledPartitionMovementNativeHost host,
        PartitionHostApplicationOwner? owner, PartitionStorageLifecycle? lower, bool refusalProved,
        List<Exception> failures)
    {
        var originalFaults = new List<Exception>();
        var originals = new Task?[OriginalCloseCount];
        await ServerFailureObserver.ObserveAsync(() =>
        {
            var original = host.DisposeAsync().AsTask();
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
            await ServerFailureObserver.ObserveAsync(() => PartitionStorageNativeStopAssertions.RequireAsync(
                host, owner, lower, originals, originalFaults), proof).ConfigureAwait(false);
        }
        if (!refusalProved || proof.Count != NoFailures)
        {
            failures.AddRange(originalFaults);
            failures.AddRange(proof);
        }
    }
}
