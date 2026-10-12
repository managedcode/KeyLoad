using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class ComposedProjectionLineageFlow
{
    internal const string Reader = "projection-lineage-reader";

    private static AstQueryRequest Request(PartitionRef partition)
    {
        var original = SqlQueueReferenceJoinFixture.Ast(partition);
        return original with
        {
            Query = original.Query with
            {
                Projection = [new("/id", "message_id", "q"), new("/payload/to/id", "reference_id", "q"),
                new("/name", "customer_name", "d"), new("/payload/score", "score", "q")]
            }
        };
    }

    internal static async Task VerifyAsync(DatabaseEngine owner, ZoneTreeStore store, PartitionRef partition, bool masked, bool repaired, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var engine = new QueryEngine(owner, UnitExecutionOptions.QueryExecution());
        var ast = Request(partition);
        var sql = SqlQueueReferenceJoinFixture.Request(partition);
        sql = sql with
        {
            Sql = sql.Sql.Replace("d.name AS customer_name",
            "q.payload.to.id AS reference_id, d.name AS customer_name", StringComparison.Ordinal)
        };
        var sqlPage = engine.Execute(Reader, sql, cancellationToken: token);
        var astPage = engine.ExecuteAst(Reader, ast, cancellationToken: token);
        await ComposedProjectionLineageAssertions.PageAsync(sqlPage, position, masked, repaired);
        await ComposedProjectionLineageAssertions.PageAsync(astPage, position, masked, repaired);
        await Assert.That(JsonDefaults.Serialize(astPage)).IsEquivalentTo(JsonDefaults.Serialize(sqlPage), global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        foreach (var selection in new Selection[]
        {
            new("/name", "forged", "foreign"),
            new("/name", "forged", "q"),
            new("/payload/to/id", "forged", "d")
        })
        {
            var invalid = ast with { Query = ast.Query with { Projection = [selection] } };
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(Reader, invalid, cancellationToken: token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.UnsupportedCapability);
            await Assert.That(store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(store)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    internal static async Task DeniedAsync(TestDatabase database, CancellationToken token)
    {
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ExecuteAst(Reader, Request(database.Partition), cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        var sqlFailure = Assert.ThrowsExactly<KeyLoadException>(() => engine.Execute(Reader,
            SqlQueueReferenceJoinFixture.Request(database.Partition), cancellationToken: token));
        await Assert.That(sqlFailure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
