using KeyLoad.Server;
using KeyLoad.Storage;
using P = KeyLoad.UnitTests.Features.StorageRecovery.PartitionStorageNativeProducerDisposeProtocol;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class PartitionStorageNativeProducerDisposeAssertions
{
    internal static async Task BeforeAsync(PartitionStorageNativeProducerDisposeState state)
    {
        await Assert.That(state.Owner.Opening is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(state.Owner.StorageProducerJoins is null).IsTrue();
        await Assert.That(state.Connections).IsNotNull();
    }

    internal static async Task ClosedAsync(PartitionStorageNativeProducerDisposeState state, Task originalClose)
    {
        await Assert.That(originalClose.IsCompletedSuccessfully).IsTrue();
        await Assert.That(state.Owner.Closing is { IsCompletedSuccessfully: true }).IsTrue();
        await Assert.That(state.Owner.CanReleaseRoot).IsTrue();
        await Assert.That(state.Node.HasJoinedRequestWork).IsTrue();
        var joins = state.Owner.StorageProducerJoins ?? throw new InvalidOperationException(P.Missing);
        await Assert.That(joins.Count).IsEqualTo(P.ProducerCount);
        await Assert.That(ReferenceEquals(joins[P.Recurring], state.Recurring.JoinStorageProducerAsync())).IsTrue();
        await Assert.That(ReferenceEquals(joins[P.Chunks], state.Chunks.JoinStorageProducerAsync())).IsTrue();
        await Assert.That(ReferenceEquals(joins[P.Ann], state.Ann.JoinStorageProducerAsync())).IsTrue();
        foreach (var original in joins)
        {
            await Assert.That(original.IsCompletedSuccessfully).IsTrue();
        }
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

    internal static async Task IdentityAsync(StoreIdentity expected, StoreIdentity actual)
    {
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected)))
            .IsTrue();
    }
}
