using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlGraphPathParameterBoundaryTests
{
    private const string GraphParameter = "graph";
    private const string FromCollectionParameter = "fromCollection";
    private const string FromIdParameter = "fromId";
    private const string ToCollectionParameter = "toCollection";
    private const string ToIdParameter = "toId";
    private const string DepthParameter = "depth";
    private const string VerticesParameter = "vertices";
    private const string EdgesParameter = "edges";
    private const string LabelParameter = "label";
    [Test]
    public async Task SignedInt32BoundariesParseBeforeOperationCapsReject()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = new QueryEngine(database.Database);
        var token = TestContext.Current!.Execution.CancellationToken;
        foreach (var boundary in new[] { int.MinValue, int.MaxValue })
        {
            await AssertFailureCode(engine, SqlGraphPathTestSupport.Request(database, LiteralSql(boundary)),
                ErrorCode.BudgetExceeded, token);
            await AssertFailureCode(engine, SqlGraphPathTestSupport.Request(database,
                SqlGraphPathTestSupport.ParameterSql, Parameters(boundary)), ErrorCode.BudgetExceeded, token);
        }
    }

    [Test]
    public async Task FractionalAndOutOfRangeNumericFormsFailValidation()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = new QueryEngine(database.Database);
        var token = TestContext.Current!.Execution.CancellationToken;
        foreach (var depth in new[] { "2.5", "2e0", "2147483648", "-2147483649" })
        {
            await AssertFailureCode(engine, SqlGraphPathTestSupport.Request(database, LiteralSql(depth)),
                ErrorCode.Validation, token);
        }
        foreach (var rawNumber in new[] { "2.5", "2147483648", "-2147483649", "3e20" })
        {
            await AssertFailureCode(engine, SqlGraphPathTestSupport.Request(database,
                SqlGraphPathTestSupport.ParameterSql, Parameters(Number(rawNumber))), ErrorCode.Validation, token);
        }
    }

    [Test]
    public async Task SixtyFourUniqueTrailingLabelsAdmitTheSameOrderedPathAndSixtyFiveReject()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = new QueryEngine(database.Database);
        var token = TestContext.Current!.Execution.CancellationToken;
        var maximumLabels = SqlGraphPathTestSupport.LiteralSql;
        var labels = Enumerable.Range(0, 64)
            .Select(index => index == 0 ? SqlGraphPathTestSupport.Label : $"unused-{index}");
        maximumLabels = WithLabels(maximumLabels, labels);
        var actual = engine.ShortestPathSql(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Request(database, maximumLabels), cancellationToken: token);
        var direct = database.Database.ShortestPath(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Direct(database, ImmutableArray.Create(SqlGraphPathTestSupport.Label)),
            cancellationToken: token);

        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(direct))).IsTrue();
        await Assert.That(actual.Edges.Select(edge => edge.Id)
            .SequenceEqual([SqlGraphPathTestSupport.FirstEdge, SqlGraphPathTestSupport.SecondEdge])).IsTrue();
        await AssertFailureCode(engine, SqlGraphPathTestSupport.Request(database,
            WithLabels(SqlGraphPathTestSupport.LiteralSql, Enumerable.Range(0, 65)
                .Select(index => index == 0 ? SqlGraphPathTestSupport.Label : $"unused-{index}"))),
            ErrorCode.Validation, token);
    }

    [Test]
    public async Task OneTerminalSemicolonIsAllowedButASecondTerminatorRejects()
    {
        using var database = SqlGraphPathTestSupport.CreateSeededDatabase();
        var engine = new QueryEngine(database.Database);
        var token = TestContext.Current!.Execution.CancellationToken;
        var terminated = engine.ShortestPathSql(SqlGraphPathTestSupport.Principal,
            SqlGraphPathTestSupport.Request(database, SqlGraphPathTestSupport.LiteralSql + ";"),
            cancellationToken: token);

        await Assert.That(terminated.Edges.Select(edge => edge.Id)
            .SequenceEqual([SqlGraphPathTestSupport.FirstEdge, SqlGraphPathTestSupport.SecondEdge])).IsTrue();
        await AssertFailureCode(engine, SqlGraphPathTestSupport.Request(database,
            SqlGraphPathTestSupport.LiteralSql + ";;"), ErrorCode.Validation, token);
    }

    private static Dictionary<string, JsonElement> Parameters(object depth)
        => SqlGraphPathTestSupport.Parameters(
            (GraphParameter, SqlGraphPathTestSupport.Graph),
            (FromCollectionParameter, SqlGraphPathTestSupport.Orders),
            (FromIdParameter, SqlGraphPathTestSupport.StartId),
            (ToCollectionParameter, SqlGraphPathTestSupport.Orders),
            (ToIdParameter, SqlGraphPathTestSupport.TargetId),
            (DepthParameter, depth),
            (VerticesParameter, SqlGraphPathTestSupport.MaximumVertices),
            (EdgesParameter, SqlGraphPathTestSupport.MaximumEdges),
            (LabelParameter, SqlGraphPathTestSupport.Label));

    private static JsonElement Number(string rawNumber)
    {
        using var document = JsonDocument.Parse(rawNumber);
        return document.RootElement.Clone();
    }

    private static string LiteralSql(int depth)
        => LiteralSql(depth.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static string LiteralSql(string depth)
        => $"SELECT * FROM GRAPH_SHORTEST_PATH('{SqlGraphPathTestSupport.Graph}', "
            + $"'{SqlGraphPathTestSupport.Orders}', '{SqlGraphPathTestSupport.StartId}', "
            + $"'{SqlGraphPathTestSupport.Orders}', '{SqlGraphPathTestSupport.TargetId}', {depth}, "
            + $"{SqlGraphPathTestSupport.MaximumVertices}, {SqlGraphPathTestSupport.MaximumEdges})";

    private static string WithLabels(string sql, IEnumerable<string> labels)
        => sql[..^1] + ", " + string.Join(", ", labels.Select(label => $"'{label}'")) + ")";

    private static async Task AssertFailureCode(QueryEngine engine, SqlGraphPathRequest request,
        ErrorCode expectedCode, CancellationToken token)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => engine.ShortestPathSql(
            SqlGraphPathTestSupport.Principal, request, cancellationToken: token));
        await Assert.That(failure.Code).IsEqualTo(expectedCode);
    }
}
