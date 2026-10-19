using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class RetainedAppendIdentityOperations
{
    internal static async Task RefuseAsync(DatabaseEngine database, ZoneTreeStore store, CommandRequest command,
        string[] image, long position, StoreIdentity identity)
    {
        var failures = new List<Exception>();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var error = Assert.ThrowsExactly<KeyLoadException>(() => QueueWholeFlowStorage.Apply(database,
                    OperationKind.Batch, command, command.CommandId, RetainedAppendIdentityFixture.Epoch));
                await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
                await Assert.That(error.Message).IsEqualTo(RetainedAppendIdentityFixture.Invalid);
                await Assert.That(store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(command.Partition,
                    "root", command.CommandId)))).IsNull();
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => RetainedAppendIdentityAssertions.ConservedAsync(store,
                image, position, identity), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task ReplayAsync(DatabaseEngine database, ZoneTreeStore store, RetainedAppendIdentityFixture fixture)
    {
        var image = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = ReadCutStoreStateAssertions.SnapshotIdentity(store);
        var failures = new List<Exception>();
        foreach (var original in fixture.Successes)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var key = KeySpace.PartitionOutcome(fixture.Stream.Partition, "root", original.Key.CommandId);
                var raw = store.Read(view => view.ReadOwnedValue(key))
                    ?? throw new InvalidOperationException("The original complete outcome is missing.");
                await ReplayResultAssertions.Same<CommitReceipt>(QueueWholeFlowStorage.Apply(database,
                    OperationKind.Batch, original.Key, original.Key.CommandId, RetainedAppendIdentityFixture.Epoch), original.Value);
                await Assert.That(store.Read(view => view.ReadOwnedValue(key))!.AsSpan().SequenceEqual(raw)).IsTrue();
                foreach (var effect in original.Value.Get<CommitReceipt>().Mutations)
                {
                    await Assert.That(effect.CompositionReferences).IsEmpty();
                }
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => RetainedAppendIdentityAssertions.ConservedAsync(store,
                image, position, identity), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static MutationReceipt[] Effects(string id, long tail) => [
        new("putDocument", RetainedAppendIdentityFixture.Documents, id, 1),
        new("enqueue", RetainedAppendIdentityFixture.Queue, id, 1),
        new("appendEvents", RetainedAppendIdentityFixture.Streams, RetainedAppendIdentityFixture.Target, tail)];
}
