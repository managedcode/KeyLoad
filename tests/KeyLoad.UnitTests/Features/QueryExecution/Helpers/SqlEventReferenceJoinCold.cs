using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Server;
using KeyLoad.Orleans;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlEventReferenceJoinCold
{
    private const int ColdCuts = 2;

    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt original,
        CommandRequest repair, CommitReceipt repaired)
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
                using var reopened = ZoneTreeExistingStore.Open(new(database.Directory) { Incarnation = identity.Incarnation },
                    identity.NodeId, database.StorageExecution);
                try
                {
                    var owner = database.CreateConfiguredDatabase(reopened);
                    await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
                    await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                    var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
                    await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute("root",
                        SqlEventReferenceJoinFixture.Request(database.Partition)), position, repaired: true);
                    await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute(SqlEventReferenceJoinFixture.Reader,
                        SqlEventReferenceJoinFixture.Request(database.Partition)), position, restricted: true, repaired: true);
                    var replay = owner.ApplyEmbedded(new(seed.CommandId, OperationKind.Batch, "root", default,
                        JsonSerializer.Serialize(seed, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
                    await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(JsonDefaults.Serialize(original), CollectionOrdering.Matching);
                    var repairReplay = owner.ApplyEmbedded(new(repair.CommandId, OperationKind.Batch, "root", default,
                        JsonSerializer.Serialize(repair, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
                    await Assert.That(JsonDefaults.Serialize(repairReplay)).IsEquivalentTo(JsonDefaults.Serialize(repaired), CollectionOrdering.Matching);
                    var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
                        SqlEventReferenceJoinFixture.Request(database.Partition, SqlEventReferenceJoinFixture.InvalidStreamSet)));
                    await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
                    await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, CollectionOrdering.Matching);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
                { failures.Add(error); }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
                { failures.Add(error); }
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            { failures.Add(error); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }
}
