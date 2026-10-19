using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageProducerDisposeAssertions
{
    internal static async Task BeforeAsync(PartitionStorageProducerDisposeState state)
    {
        await Assert.That(state.Owner.Opening is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(state.Owner.Closing is null).IsTrue();
        await Assert.That(state.Node.Closing is null).IsTrue();
        await Assert.That(state.Node.SiloJoined).IsTrue();
        await Assert.That(state.Node.DatabaseReady).IsTrue();
        await Assert.That(state.Node.Grains).IsNotNull();
        await Assert.That(state.Connections).IsNotNull();
    }

    internal static async Task ClosedAsync(PartitionStorageProducerDisposeState state, Task originalClose)
    {
        await Assert.That(originalClose.IsCompletedSuccessfully).IsTrue();
        await Assert.That(state.Owner.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(state.Node.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(state.Node.RequestWork.IsJoined).IsTrue();
        await Assert.That(state.Owner.CanReleaseRoot).IsTrue();
        await Assert.That(state.Node.HasJoinedRequestWork).IsTrue();
        var failures = new List<Exception>();
        FileStream? reacquired = null;
        ServerFailureObserver.Observe(() => reacquired = new FileStream(
            Path.Combine(state.Directory, PartitionStoreProtocol.OwnershipFile), FileMode.Open,
            FileAccess.ReadWrite, FileShare.None), failures);
        if (reacquired is not null)
        {
            ServerFailureObserver.Observe(reacquired.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
