using System.Collections.Immutable;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphPathParityTests
{
    [Test]
    public async Task LiteralGraphPathSqlMatchesTheDirectZoneTreeOperatorAndReadCut()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var direct = database.Database.ShortestPath(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Direct(database),
            cancellationToken: cancellationToken);
        var actual = new QueryEngine(database.Database).ShortestPathSql(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql),
            cancellationToken: cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(direct))).IsTrue();
        await Assert.That(actual.Found).IsTrue();
        await Assert.That(actual.Hops).IsEqualTo((int?)SqlGraphPathTestSupport.PathDepth);
        var edgeIds = actual.Edges.Select(edge => edge.Id).ToArray();
        var expectedEdgeIds = new[] { SqlGraphPathTestSupport.FirstEdge, SqlGraphPathTestSupport.SecondEdge };
        await Assert.That(edgeIds.SequenceEqual(expectedEdgeIds)).IsTrue();
    }

    [Test]
    public async Task ExactStringAndInt32ParametersAndTrailingLabelMatchTheDirectOperator()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var parameters = SqlGraphPathTestSupport.Parameters(
            ("graph", SqlGraphPathTestSupport.Graph),
            ("fromCollection", SqlGraphPathTestSupport.Orders),
            ("fromId", SqlGraphPathTestSupport.StartId),
            ("toCollection", SqlGraphPathTestSupport.Orders),
            ("toId", SqlGraphPathTestSupport.TargetId),
            ("depth", SqlGraphPathTestSupport.PathDepth),
            ("vertices", SqlGraphPathTestSupport.MaximumVertices),
            ("edges", SqlGraphPathTestSupport.MaximumEdges), ("label", SqlGraphPathTestSupport.Label));
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var direct = database.Database.ShortestPath(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Direct(database, ImmutableArray.Create(SqlGraphPathTestSupport.Label)),
            cancellationToken: cancellationToken);
        var actual = new QueryEngine(database.Database).ShortestPathSql(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.ParameterSql, parameters),
            cancellationToken: cancellationToken);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(direct))).IsTrue();
    }
}
