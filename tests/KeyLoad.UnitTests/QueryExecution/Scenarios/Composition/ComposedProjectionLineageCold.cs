using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ComposedProjectionLineageCold
{
    private const int ColdCuts = 2;

    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt original, CommandRequest repair, CommitReceipt repaired, CancellationToken token)
    {
        var identity = database.Store.Identity;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        database.Store.Dispose();
        for (var cut = 0; cut < ColdCuts; cut++)
        {
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var reopened = ZoneTreeExistingStore.Open(new(database.Directory)
                {
                    Incarnation = identity.Incarnation
                },
                    identity.NodeId, database.StorageExecution);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
                    await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                    await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    var owner = database.CreateConfiguredDatabase(reopened);
                    await ComposedProjectionLineageFlow.VerifyAsync(owner, reopened, database.Partition, masked: false, repaired: true, token);
                    var replay = owner.ApplyEmbedded(owner.CreateCommand(OperationKind.Batch, seed.CommandId, "root", default, JsonSerializer.Serialize(seed, JsonDefaults.Options)), cancellationToken: token).Get<CommitReceipt>();
                    await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(JsonDefaults.Serialize(original), global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    var repairReplay = owner.ApplyEmbedded(owner.CreateCommand(OperationKind.Batch, repair.CommandId, "root", default, JsonSerializer.Serialize(repair, JsonDefaults.Options)), cancellationToken: token).Get<CommitReceipt>();
                    await Assert.That(JsonDefaults.Serialize(repairReplay)).IsEquivalentTo(JsonDefaults.Serialize(repaired), global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
