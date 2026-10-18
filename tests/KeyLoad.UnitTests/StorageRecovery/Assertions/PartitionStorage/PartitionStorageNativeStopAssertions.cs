using KeyLoad.RecoveryHost.Features.ClusterRouting;
using KeyLoad.Server;
using C = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativePartialClose;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeStopAssertions
{
    private const int NoFaults = 0;
    private const int ExpectedProducerCount = 3;
    private const string StartingStop =
        "Attempted to shutdown a silo which is not in the Running state. This silo is in the Starting state.";

    internal static async Task RequireAsync(ControlledPartitionMovementNativeHost host,
        PartitionHostApplicationOwner? owner, PartitionStorageLifecycle? lower, Task?[] originals,
        IReadOnlyList<Exception> originalFaults)
    {
        await Assert.That(owner is not null && lower is not null).IsTrue();
        var physical = owner!;
        var late = lower!;
        await Assert.That(originals[C.HostCloseIndex] is { IsFaulted: true }).IsTrue();
        await Assert.That(originalFaults.Count > NoFaults).IsTrue();
        await RequireOriginalAsync(originals[C.HostCloseIndex]).ConfigureAwait(false);
        await Assert.That(originals[C.LowerCloseIndex] is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(originals[C.OwnerCloseIndex] is { IsCompletedSuccessfully: true }).IsTrue();
        foreach (var original in originalFaults)
        {
            await RequireLeafAsync(original).ConfigureAwait(false);
        }
        await Assert.That(ReferenceEquals(originals[C.HostCloseIndex], host.DisposeAsync().AsTask())).IsTrue();
        await Assert.That(ReferenceEquals(originals[C.LowerCloseIndex], late.StopAsync(CancellationToken.None)))
            .IsTrue();
        await Assert.That(ReferenceEquals(originals[C.OwnerCloseIndex], physical.Closing)).IsTrue();
        await Assert.That(physical.CanReleaseRoot).IsTrue();
        await Assert.That(host.IsJoined).IsTrue();
        var producers = physical.StorageProducerJoins;
        await Assert.That(producers is not null).IsTrue();
        var joined = producers!;
        await Assert.That(joined.Count).IsEqualTo(ExpectedProducerCount);
        foreach (var original in joined)
        {
            await Assert.That(original.IsCompletedSuccessfully).IsTrue();
        }
    }

    private static async Task RequireOriginalAsync(Task? original)
    {
        await Assert.That(original is { IsCompleted: true }).IsTrue();
        var completed = original!;
        if (completed.IsCompletedSuccessfully)
        {
            return;
        }
        await Assert.That(completed.IsFaulted).IsTrue();
        await Assert.That(completed.Exception is not null).IsTrue();
        foreach (var failure in completed.Exception!.InnerExceptions)
        {
            await RequireLeafAsync(failure).ConfigureAwait(false);
        }
    }

    private static async Task RequireLeafAsync(Exception original)
    {
        if (original is AggregateException aggregate && aggregate.InnerExceptions.Count > NoFaults)
        {
            foreach (var failure in aggregate.InnerExceptions)
            {
                await RequireLeafAsync(failure).ConfigureAwait(false);
            }
            return;
        }
        await Assert.That(original.GetType()).IsEqualTo(typeof(InvalidOperationException));
        await Assert.That(original.Message).IsEqualTo(StartingStop);
        await Assert.That(original.InnerException).IsNull();
    }
}
