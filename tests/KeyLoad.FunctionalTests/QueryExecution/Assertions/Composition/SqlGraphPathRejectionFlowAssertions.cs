using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class SqlGraphPathRejectionFlowAssertions
{
    private const string ExpectedAttributesJson = "{}";
    private const string Principal = SqlGraphPathTestSupport.Principal;

    internal static Task AssertRejectThenRetryAsync(TestDatabase database, QueryEngine engine,
        SqlGraphPathRequest invalidRequest, CancellationToken cancellationToken)
        => AssertRejectThenRetryAsync(database, engine, invalidRequest, ErrorCode.Validation, null, cancellationToken);

    internal static async Task AssertRejectThenRetryAsync(TestDatabase database, QueryEngine engine,
        SqlGraphPathRequest invalidRequest, ErrorCode expectedCode, string? expectedDetail,
        CancellationToken cancellationToken)
    {
        var committedPosition = database.Store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPathSql(Principal,
            invalidRequest, cancellationToken: cancellationToken));
        await Assert.That(failure.Code).IsEqualTo(expectedCode);
        if (expectedDetail is not null)
        {
            await Assert.That(failure.Message).IsEqualTo(expectedDetail);
        }
        await Assert.That(database.Store.Position).IsEqualTo(committedPosition);

        var healthy = engine.ShortestPathSql(Principal,
            SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql),
            cancellationToken: cancellationToken);
        var direct = database.Database.ShortestPath(Principal, SqlGraphPathTestSupport.Direct(database),
            cancellationToken: cancellationToken);
        await Assert.That(JsonDefaults.Serialize(healthy).AsSpan()
            .SequenceEqual(JsonDefaults.Serialize(direct))).IsTrue();
        await AssertIndependentPathAsync(database, healthy, direct, committedPosition);
        await Assert.That(database.Store.Position).IsEqualTo(committedPosition);
    }

    private static async Task AssertIndependentPathAsync(TestDatabase database, GraphShortestPathResult actual,
        GraphShortestPathResult direct, long committedPosition)
    {
        var from = database.ExpectedReference(new(database.Partition, SqlGraphPathTestSupport.Orders, SqlGraphPathTestSupport.StartId));
        var middle = database.ExpectedReference(new(database.Partition, SqlGraphPathTestSupport.Projects, SqlGraphPathTestSupport.MiddleId));
        var to = database.ExpectedReference(new(database.Partition, SqlGraphPathTestSupport.Orders, SqlGraphPathTestSupport.TargetId));
        var expectedVertices = new[] { from, middle, to };
        var expectedEdges = new[]
        {
            new EdgeRecord(SqlGraphPathTestSupport.FirstEdge, from, middle, SqlGraphPathTestSupport.Label,
                ExpectedAttributesJson, 1),
            new EdgeRecord(SqlGraphPathTestSupport.SecondEdge, middle, to, SqlGraphPathTestSupport.Label,
                ExpectedAttributesJson, 1)
        };
        await Assert.That(actual.Found).IsTrue();
        await Assert.That(actual.Hops).IsEqualTo((int?)SqlGraphPathTestSupport.PathDepth);
        await Assert.That(actual.Vertices.SequenceEqual(expectedVertices)).IsTrue();
        await Assert.That(actual.Edges.SequenceEqual(expectedEdges)).IsTrue();
        await Assert.That(actual.CutPosition).IsEqualTo(committedPosition);
        await Assert.That(direct.CutPosition).IsEqualTo(committedPosition);
    }
}
