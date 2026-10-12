using KeyLoad.Query;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlEventReferenceJoinOperation
{
    internal static async Task VerifyAsync(TestDatabase database, bool repaired = false)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var before = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var request = SqlEventReferenceJoinFixture.Request(database.Partition);
        var page = engine.Execute("root", request);
        await SqlEventReferenceJoinAssertions.PageAsync(page, position, repaired: repaired);
        var ast = engine.ExecuteAst("root", SqlEventReferenceJoinFixture.Ast(database.Partition));
        await Assert.That(JsonDefaults.Serialize(ast)).IsEquivalentTo(JsonDefaults.Serialize(page), CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(before, CollectionOrdering.Matching);
    }

    internal static async Task RefusalsAsync(TestDatabase database)
    {
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var request = SqlEventReferenceJoinFixture.Request(database.Partition);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with { Parameters = null }), ErrorCode.Validation);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", request with { Sql = request.Sql.Replace("e.payload.score >= @minimum", "d.name = @minimum", StringComparison.Ordinal) }),
            ErrorCode.UnsupportedCapability);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", SqlEventReferenceJoinFixture.Request(database.Partition, SqlEventReferenceJoinFixture.InvalidStreamSet)),
            ErrorCode.Validation);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.ExecuteAst("root", SqlEventReferenceJoinFixture.Ast(database.Partition) with
            {
                Query = SqlEventReferenceJoinFixture.Ast(database.Partition).Query with
                { InnerJoin = new("missingcustomers", "d", "/payload/to", "/entity_ref") }
            }), ErrorCode.NotFound);
        await SqlEventReferenceJoinAssertions.RejectAsync(database,
            () => engine.Execute("root", SqlEventReferenceJoinFixture.Request(database.Partition, generation: 2)),
            ErrorCode.TokenInvalidated);
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
