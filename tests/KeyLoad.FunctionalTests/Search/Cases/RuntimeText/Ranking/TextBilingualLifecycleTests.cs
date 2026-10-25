using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextBilingualLifecycleTests
{
    private const long OriginalRevision = 1;
    private const long DeletedRevision = 2;
    private const string DeleteKind = "deleteDocument";

    [Test]
    public async Task LiteralUkrainianEnglishDeleteReplayAndReopenPreserveAuthorizedResults()
    {
        using var database = new TestDatabase();
        database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
        database.Commit(new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId,
                TextBilingualAudit.UkrainianJson),
            new PutDocument(TextBilingualAudit.Collection, TextBilingualAudit.EnglishId, TextBilingualAudit.EnglishJson));
        var token = TestContext.Current!.Execution.CancellationToken;
        using (var projection = TextBilingualAudit.Open(database))
        {
            var search = new SearchEngine(database.Database, UnitExecutionOptions.QueryExecution(), projection);
            await TextBilingualAudit.VerifyAsync(search, database, token);
            var command = new CommandRequest(Guid.NewGuid(), database.Partition,
                [new DeleteDocument(TextBilingualAudit.Collection, TextBilingualAudit.UkrainianId, OriginalRevision)]);
            var receipt = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
            await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
            await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
            await Assert.That(receipt.Token.Position).IsEqualTo(database.Database.LastApplied);
            var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
            var expected = new MutationReceipt(DeleteKind, TextBilingualAudit.Collection,
                TextBilingualAudit.UkrainianId, DeletedRevision);
            await Assert.That(OrleansSerialization.Serialize(mutation).SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
            var position = database.Store.Position;
            var bytes = QueueWholeFlowStorage.Bytes(database.Store);
            var replay = database.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
            await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(receipt))).IsTrue();
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(bytes, CollectionOrdering.Matching);
            await TextBilingualAudit.VerifyDeletedAsync(search, database, token);
        }
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var owner = TextBilingualAudit.Owner(reopened);
        using var restoredProjection = TextBilingualAudit.Open(database);
        var restored = new SearchEngine(owner, UnitExecutionOptions.QueryExecution(), restoredProjection);
        await TextBilingualAudit.VerifyDeletedAsync(restored, database, token);
        var image = QueueWholeFlowStorage.Bytes(reopened);
        var cut = reopened.Position;
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => restored.Search(TextBilingualAudit.Denied,
            TextBilingualAudit.Request(database.Partition, TextBilingualAudit.EnglishQuery), token));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(reopened.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(image, CollectionOrdering.Matching);
        await TextBilingualAudit.VerifyDeletedAsync(restored, database, token);
        await Assert.That(reopened.Position).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(image, CollectionOrdering.Matching);
    }
}
