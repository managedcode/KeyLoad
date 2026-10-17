using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextBilingualLifecycleTests
{
    private const long OriginalRevision = 1;
    private const long DeletedRevision = 2;
    private const string DeleteKind = "deleteDocument";

    [Test]
    public async Task Kl028LiteralUkrainianEnglishDeleteReplayAndNativeReopenPreserveAuthorizedResults()
    {
        using var database = new TestDatabase();
        database.Configure(NativeTextBilingualAudit.Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId,
                NativeTextBilingualAudit.UkrainianJson),
            new PutDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.EnglishId, NativeTextBilingualAudit.EnglishJson));
        var token = TestContext.Current!.Execution.CancellationToken;
        using (var projection = NativeTextBilingualAudit.Open(database))
        {
            var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            await NativeTextBilingualAudit.VerifyAsync(search, database.Partition, token);
            var command = new CommandRequest(Guid.NewGuid(), database.Partition,
                [new DeleteDocument(NativeTextBilingualAudit.Collection, NativeTextBilingualAudit.UkrainianId, OriginalRevision)]);
            var receipt = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
            await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
            await Assert.That(receipt.Token.Position).IsEqualTo(database.Store.Position);
            var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
            var expected = new MutationReceipt(DeleteKind, NativeTextBilingualAudit.Collection,
                NativeTextBilingualAudit.UkrainianId, DeletedRevision);
            await Assert.That(NativeSerialization.Serialize(mutation).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
            var position = database.Store.Position;
            var bytes = QueueWholeFlowStorage.Bytes(database.Store);
            var replay = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
            await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
            await NativeTextBilingualAudit.VerifyDeletedAsync(search, database.Partition, token);
        }
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var owner = NativeTextBilingualAudit.Owner(reopened);
        using var restoredProjection = NativeTextBilingualAudit.Open(database);
        var restored = new SearchEngine(owner, UnitExecutionOptions.QueryExecution(), restoredProjection);
        await NativeTextBilingualAudit.VerifyDeletedAsync(restored, database.Partition, token);
        var image = QueueWholeFlowStorage.Bytes(reopened);
        var cut = reopened.Position;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => restored.Search(NativeTextBilingualAudit.Denied,
            NativeTextBilingualAudit.Request(database.Partition, NativeTextBilingualAudit.EnglishQuery), token));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(reopened.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await NativeTextBilingualAudit.VerifyDeletedAsync(restored, database.Partition, token);
        await Assert.That(reopened.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
