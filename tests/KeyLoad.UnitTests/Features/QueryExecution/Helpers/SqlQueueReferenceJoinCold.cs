using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlQueueReferenceJoinCold
{
    private const int ColdCuts = 2;

    internal static async Task RunAsync(TestDatabase database, CommandRequest seed, CommitReceipt original)
    {
        var identity = database.Store.Identity;
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        database.Store.Dispose();
        for (var cut = 0; cut < ColdCuts; cut++)
        { await OpenAsync(database, seed, original, before, identity.NodeId, identity.Incarnation, position); }
    }

    private static async Task OpenAsync(TestDatabase database, CommandRequest seed, CommitReceipt original,
        string[] before, Guid nodeId, Guid incarnation, long position)
    {
        var failures = new List<Exception>();
        try
        {
            using var reopened = ZoneTreeExistingStore.Open(new(database.Directory) { Incarnation = incarnation },
                nodeId, database.StorageExecution);
            try
            {
                var owner = database.CreateConfiguredDatabase(reopened);
                await Assert.That(reopened.Identity.NodeId).IsEqualTo(nodeId);
                await Assert.That(reopened.Identity.Incarnation).IsEqualTo(incarnation);
                await Assert.That(reopened.Position).IsEqualTo(position);
                var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
                await SqlQueueReferenceJoinAssertions.PageAsync(engine.Execute("root",
                    SqlQueueReferenceJoinFixture.Request(database.Partition)), position, repaired: true);
                await SqlQueueReferenceJoinAssertions.PageAsync(engine.Execute(SqlQueueReferenceJoinFixture.Reader,
                    SqlQueueReferenceJoinFixture.Request(database.Partition)), position, restricted: true, repaired: true);
                var replay = owner.ApplyEmbedded(new(seed.CommandId, OperationKind.Batch, "root", default,
                    JsonSerializer.Serialize(seed, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
                await Assert.That(JsonDefaults.Serialize(replay)).IsEquivalentTo(JsonDefaults.Serialize(original), CollectionOrdering.Matching);
                var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
                    SqlQueueReferenceJoinFixture.Request(database.Partition, SqlQueueReferenceJoinFixture.InvalidQueue)));
                await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
                var filtered = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute("root",
                    SqlQueueReferenceJoinFixture.FilteredInvalidRequest(database.Partition)));
                await Assert.That(filtered.Code).IsEqualTo(ErrorCode.Validation);
                await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, CollectionOrdering.Matching);
                await Assert.That(reopened.Position).IsEqualTo(position);
                var filteredAst = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst("root",
                    SqlQueueReferenceJoinFixture.FilteredInvalidAst(database.Partition)));
                await Assert.That(filteredAst.Code).IsEqualTo(ErrorCode.Validation);
                await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(before, CollectionOrdering.Matching);
                await Assert.That(reopened.Position).IsEqualTo(position);
            }
            catch (Exception failure) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { failures.Add(failure); }
            catch (Exception failure) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { failures.Add(failure); }
        }
        catch (Exception failure) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        { failures.Add(failure); }
        catch (Exception failure) when (!global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
        { failures.Add(failure); }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
