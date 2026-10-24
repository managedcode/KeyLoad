using KeyLoad.Query;
using KeyLoad.UnitTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlQueueReferenceJoinOperation
{
    internal static async Task VerifyAsync(TestDatabase database, bool repaired = false)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        await ModelJoinAliasAdmissionFlow.RunAsync(engine, database.Store, database.Partition, events: false, repaired: repaired);
        var request = SqlQueueReferenceJoinFixture.Request(database.Partition);
        var page = engine.Execute("root", request);
        await SqlQueueReferenceJoinAssertions.PageAsync(page, position, repaired: repaired);
        var ast = engine.ExecuteAst("root", SqlQueueReferenceJoinFixture.Ast(database.Partition));
        await Assert.That(JsonDefaults.Serialize(ast)).IsEquivalentTo(JsonDefaults.Serialize(page), CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    internal static async Task RefusalsAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = SqlQueueReferenceJoinFixture.Request(database.Partition);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with { Parameters = null }), ErrorCode.Validation);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with { Sql = request.Sql.Replace("q.payload.score >= @minimum", "d.name = @minimum", StringComparison.Ordinal) }),
            ErrorCode.UnsupportedCapability);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", SqlQueueReferenceJoinFixture.Request(database.Partition, SqlQueueReferenceJoinFixture.InvalidQueue)),
            ErrorCode.Validation);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", SqlQueueReferenceJoinFixture.FilteredInvalidRequest(database.Partition)), ErrorCode.Validation);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.ExecuteAst("root", SqlQueueReferenceJoinFixture.FilteredInvalidAst(database.Partition)), ErrorCode.Validation);
        await SqlQueueReferenceJoinAssertions.RejectAsync(database,
            () => engine.ExecuteAst("root", SqlQueueReferenceJoinFixture.Ast(database.Partition) with
            {
                Query = SqlQueueReferenceJoinFixture.Ast(database.Partition).Query with
                {
                    InnerJoin = new("missingcustomers", "d", "/payload/to", "/entity_ref")
                }
            }), ErrorCode.NotFound);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => engine.Execute("root", request, cancellationToken: cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
        await VerifyAsync(database);
    }
}
