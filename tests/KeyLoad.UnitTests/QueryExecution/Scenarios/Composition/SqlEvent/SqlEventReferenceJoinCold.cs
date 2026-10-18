using System.Text.Json;
using KeyLoad.Clustering;
using KeyLoad.Query;
using KeyLoad.Server;
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
                using var reopened = ZoneTreeExistingStore.Open(new(database.Directory)
                {
                    Incarnation = identity.Incarnation
                },
                    identity.NodeId, database.StorageExecution);
                try
                {
                    var owner = database.CreateConfiguredDatabase(reopened);
                    await Assert.That(reopened.Identity.NodeId).IsEqualTo(identity.NodeId);
                    await Assert.That(reopened.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                    var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
                    await ModelJoinAliasAdmissionFlow.RunAsync(engine, reopened, database.Partition, events: true, repaired: true);
                    await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute("root",
                        SqlEventReferenceJoinFixture.Request(database.Partition)), position, repaired: true);
                    await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute(SqlEventReferenceJoinFixture.Reader,
                        SqlEventReferenceJoinFixture.Request(database.Partition)), position, restricted: true, repaired: true);
                    var replay = owner.ApplyEmbedded(owner.CreateCommand(OperationKind.Batch, seed.CommandId, "root", default, JsonSerializer.Serialize(seed, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
                    await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(JsonDefaults.Serialize(original), CollectionOrdering.Matching);
                    var repairReplay = owner.ApplyEmbedded(owner.CreateCommand(OperationKind.Batch, repair.CommandId, "root", default, JsonSerializer.Serialize(repair, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
                    await Assert.That(JsonDefaults.Serialize(repairReplay)).IsEquivalentTo(JsonDefaults.Serialize(repaired), CollectionOrdering.Matching);
                    var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
                        SqlEventReferenceJoinFixture.Request(database.Partition, SqlEventReferenceJoinFixture.InvalidStreamSet)));
                    await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
                    await FilteredRefusalsAsync(reopened, engine, database.Partition);
                    await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, CollectionOrdering.Matching);
                    await Assert.That(reopened.Position).IsEqualTo(position);
                }
                catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
                {
                    failures.Add(error);
                }
                catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
                {
                    failures.Add(error);
                }
            }
            catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
            {
                failures.Add(error);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static async Task FilteredRefusalsAsync(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        QueryEngine engine, PartitionRef partition)
    {
        var before = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = store.Identity;
        var sql = SqlEventReferenceJoinFixture.Request(partition,
            SqlEventReferenceJoinFixture.InvalidStreamSet) with
        {
            Sql = SqlEventReferenceJoinFixture.Sql(SqlEventReferenceJoinFixture.InvalidStreamSet)
                .Replace("e.payload.score >= @minimum", "e.payload.score > 100", StringComparison.Ordinal),
            Parameters = null
        };
        var ast = SqlEventReferenceJoinFixture.Ast(partition);
        var filtered = ast with
        {
            Query = ast.Query with
            {
                Collection = SqlEventReferenceJoinFixture.InvalidStreamSet,
                Filter = new Comparison(new FieldOperand("/payload/score"), ">", new ValueOperand(JsonSerializer.SerializeToElement(100)))
            },
            Parameters = null
        };
        var operations = new Action[]
        {
            () => engine.Execute("root", sql),
            () => engine.ExecuteAst("root", filtered)
        };
        foreach (var operation in operations)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(operation);
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before, CollectionOrdering.Matching);
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(JsonDefaults.Serialize(store.Identity))
                .IsEquivalentTo(JsonDefaults.Serialize(identity), CollectionOrdering.Matching);
            await SqlEventReferenceJoinAssertions.PageAsync(engine.Execute("root",
                SqlEventReferenceJoinFixture.Request(partition)), position, repaired: true);
        }
    }
}
