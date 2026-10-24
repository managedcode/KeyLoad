using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Messaging;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlJoinAliasCollisionCold
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
            var failures = new List<Exception>();
            try
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
                    await SqlJoinAliasCollisionFlow.RunAsync(owner, reopened, database.Partition, repaired: true);
                    var replay = owner.ApplyEmbedded(owner.CreateCommand(OperationKind.Batch, seed.CommandId, "root", default, JsonSerializer.Serialize(seed, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
                    await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(JsonDefaults.Serialize(original), global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                }, failures);
            }
            catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
