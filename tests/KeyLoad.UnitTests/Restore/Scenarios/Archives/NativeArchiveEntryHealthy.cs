using ManagedCode.Communication.Commands;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.BackupRestore.NativeArchiveEntryProtocol;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class NativeArchiveEntryHealthy
{
    internal static async Task RunAsync(NativeArchiveEntryFixture fixture, StoreIdentity sourceIdentity, long cut,
        Command<DatabaseCommand> original, byte[] originalOutcome,
        Dictionary<string, byte[]> archive, CancellationToken token)
    {
        var identity = fixture.ReadTargetIdentity();
        await Assert.That(identity.NodeId).IsNotEqualTo(sourceIdentity.NodeId);
        await Assert.That(identity.Incarnation).IsNotEqualTo(sourceIdentity.Incarnation);
        await Assert.That(identity.SigningKey.Span.SequenceEqual(sourceIdentity.SigningKey.Span)).IsFalse();
        await Assert.That(identity.DispatchPaused).IsTrue();
        var store = fixture.Open(fixture.Target, identity);
        var database = BackupIndexedDocumentDedupFixture.CreateDatabase(store);
        await Assert.That(store.Position).IsEqualTo(cut + P.PositionStep);
        await NativeArchiveEntryAssertions.ContentAsync(database, fixture.Source.Partition, healthy: false, token);
        var before = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var refused = database.Apply(original);
        await Assert.That(Errors.Code(refused.Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
        await NativeArchiveEntryAssertions.ImageAsync(store, before, position);
        await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(store, fixture.Source.Partition,
            original.CommandId, originalOutcome);
        var command = new CommandRequest(Guid.NewGuid(), fixture.Source.Partition,
            [new PutDocument(P.Collection, P.Document, P.After, P.InitialRevision)]);
        var healthy = NativeArchiveEntryFlow.Operation(database, command);
        var placement = database.ReadAtomicPartitionPlacement(P.Root, fixture.Source.Partition);
        var receipt = database.Apply(healthy).Get<CommitReceipt>();
        await NativeArchiveEntryAssertions.ReceiptAsync(receipt, command, placement,
            position + P.PositionStep, P.HealthyRevision, store.Identity.Durability);
        await NativeArchiveEntryAssertions.ContentAsync(database, fixture.Source.Partition, healthy: true, token);
        await ColdAsync(fixture, identity, original, originalOutcome, healthy, receipt, token);
        await NativeArchiveEntryAssertions.ArchiveAsync(fixture.Backup, archive, token);
    }

    private static async Task ColdAsync(NativeArchiveEntryFixture fixture, StoreIdentity identity,
        Command<DatabaseCommand> original, byte[] originalOutcome, Command<DatabaseCommand> healthy, CommitReceipt receipt, CancellationToken token)
    {
        var failures = new List<Exception>();
        var first = fixture.OpenedStore();
        var rows = QueueWholeFlowStorage.Bytes(first);
        var position = first.Position;
        var outcome = BackupIndexedDocumentDedupFixture.ReadOutcomeBytes(first, fixture.Source.Partition, P.Root, healthy.CommandId);
        var replayed = BackupIndexedDocumentDedupFixture.CreateDatabase(first).Apply(healthy).Get<CommitReceipt>();
        await Assert.That(JsonDefaults.Serialize(replayed).SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
        await NativeArchiveEntryAssertions.ImageAsync(first, rows, position);
        fixture.CloseOpened(failures);
        for (var cold = 0; cold < P.ColdCycles && fixture.CanOpen; cold++)
        {
            var store = fixture.Open(fixture.Target, identity);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = BackupIndexedDocumentDedupFixture.CreateDatabase(store);
                await NativeArchiveEntryAssertions.ImageAsync(store, rows, position);
                await NativeArchiveEntryAssertions.ContentAsync(database, fixture.Source.Partition, healthy: true, token);
                await Assert.That(Errors.Code(database.Apply(original).Problem)).IsEqualTo(ErrorCode.TokenInvalidated);
                var replay = database.Apply(healthy).Get<CommitReceipt>();
                await Assert.That(JsonDefaults.Serialize(replay).SequenceEqual(JsonDefaults.Serialize(receipt))).IsTrue();
                await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(store, fixture.Source.Partition, original.CommandId, originalOutcome);
                await BackupIndexedDocumentDedupAssertions.AssertOldOutcomePreserved(store, fixture.Source.Partition, healthy.CommandId, outcome);
                await NativeArchiveEntryAssertions.ImageAsync(store, rows, position);
            }, failures);
            fixture.CloseOpened(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
