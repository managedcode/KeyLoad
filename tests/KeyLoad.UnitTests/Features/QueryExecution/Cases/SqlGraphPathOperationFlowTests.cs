using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphPathOperationFlowTests
{
    private const string ExtraStatement = "; SELECT * FROM path-orders";

    [Test]
    public async Task ExtraStatementsRejectWithoutReadsAndHealthySqlRetryMatchesDirectPath()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = new QueryEngine(database.Database, UnitExecutionOptions.QueryExecution());
        var originalPosition = database.Store.Position;
        var invalid = SqlGraphPathTestSupport.Request(database,
            SqlGraphPathTestSupport.LiteralSql + ExtraStatement);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPathSql(
            SqlGraphPathTestSupport.Principal, invalid,
            cancellationToken: TestContext.Current!.Execution.CancellationToken));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Store.Position).IsEqualTo(originalPosition);

        var healthy = engine.ShortestPathSql(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql),
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        var direct = database.Database.ShortestPath(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Direct(database),
            cancellationToken: TestContext.Current!.Execution.CancellationToken);
        await Assert.That(JsonDefaults.Serialize(healthy).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(direct))).IsTrue();
        await Assert.That(healthy.Found).IsTrue();
        await Assert.That(healthy.Hops).IsEqualTo((int?)SqlGraphPathTestSupport.PathDepth);
        var expectedVertices = new[]
        {
            new EntityRef(database.Partition, SqlGraphPathTestSupport.Orders, SqlGraphPathTestSupport.StartId),
            new EntityRef(database.Partition, SqlGraphPathTestSupport.Projects, SqlGraphPathTestSupport.MiddleId),
            new EntityRef(database.Partition, SqlGraphPathTestSupport.Orders, SqlGraphPathTestSupport.TargetId)
        };
        var expectedEdges = new[] { SqlGraphPathTestSupport.FirstEdge, SqlGraphPathTestSupport.SecondEdge };
        await Assert.That(healthy.Vertices.SequenceEqual(expectedVertices)).IsTrue();
        await Assert.That(healthy.Edges.Select(edge => edge.Id).SequenceEqual(expectedEdges)).IsTrue();
        await Assert.That(healthy.CutPosition).IsEqualTo(originalPosition);
        await Assert.That(database.Store.Position).IsEqualTo(originalPosition);
    }
}
