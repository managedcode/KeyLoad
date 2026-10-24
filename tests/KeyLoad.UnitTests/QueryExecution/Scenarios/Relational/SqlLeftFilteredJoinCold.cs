using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlLeftFilteredJoinCold
{
    private const int ColdCuts = 2;

    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt original)
    {
        var identity = database.Store.Identity;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        database.Store.Dispose();
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            using var reopened = ZoneTreeExistingStore.Open(
                new(database.Directory)
                {
                    Incarnation = identity.Incarnation
                }, identity.NodeId, database.StorageExecution);
            var owner = database.CreateConfiguredDatabase(reopened);
            await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
            await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
            await Assert.That(reopened.Position).IsEqualTo(position);
            var page = new QueryEngine(owner, UnitExecutionOptions.QueryExecution())
                .Execute("root", SqlLeftFilteredJoinFixture.Request(database.Partition));
            await SqlLeftFilteredJoinAssertions.PageAsync(page, position);
            var replay = owner.ApplyEmbedded(owner.CreateCommand(OperationKind.Batch, seed.CommandId, "root", default, JsonSerializer.Serialize(seed, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
            await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(
                JsonDefaults.Serialize(original), CollectionOrdering.Matching);
            await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(reopened.Position).IsEqualTo(position);
        }
    }
}
