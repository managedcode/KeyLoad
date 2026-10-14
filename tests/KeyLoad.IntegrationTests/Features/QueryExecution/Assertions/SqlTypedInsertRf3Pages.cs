using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlTypedInsertRf3Pages
{
    internal static async Task RunAsync(RequestCqrsRf3Callers caller, PostgresRf3Caller wire,
        SqlTypedInsertRf3State state, CancellationToken token)
    {
        var request = new SqlOperationRequest(SqlTypedInsertRf3Literal.Partition, SqlTypedInsertRf3Literal.Read, AllowFullScan: true);
        await PageAsync(state, await SqlRf3Protocol.SdkAsync<QueryPage>(caller.Sdk, request, token));
        await PageAsync(state, await SqlRf3Protocol.McpAsync<QueryPage>(caller.Mcp, request, token));
        await PageAsync(state, await wire.ExecuteAsync<QueryPage>(SqlTypedInsertRf3Literal.Read, [], prepared: false, token));
        var ast = new global::KeyLoad.Query.AstQueryRequest(SqlTypedInsertRf3Literal.Partition,
            new global::KeyLoad.Query.SelectQuery(SqlTypedInsertRf3Literal.Table, null,
                [new(IdPath, SqlTypedInsertRf3Literal.IdColumn), new(SqlTypedInsertRf3Literal.TitlePath, SqlTypedInsertRf3Literal.TitleColumn),
                    new(AmountPath, SqlTypedInsertRf3Literal.AmountColumn), new(ActivePath, SqlTypedInsertRf3Literal.ActiveColumn),
                    new(PricePath, SqlTypedInsertRf3Literal.PriceColumn), new(TimestampPath, SqlTypedInsertRf3Literal.TimestampColumn)],
                null, [new(IdPath, false)], SqlTypedInsertRf3Literal.MaximumRows), AllowFullScan: true);
        await PageAsync(state, await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.QueryAstAsync(ast, token)));
        var reference = new EntityRef(SqlTypedInsertRf3Literal.Partition, SqlTypedInsertRf3Literal.Table, SqlTypedInsertRf3Literal.One);
        await SqlRf3Protocol.EqualAsync(new DocumentReadResult(new DocumentResult(reference,
            SqlTypedInsertRf3Literal.InitialRevision, SqlTypedInsertRf3Literal.OneJson, false, [])),
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.GetAsync(reference, state.Receipt.Token, token)));
    }
    private static async Task PageAsync(SqlTypedInsertRf3State state, QueryPage page)
    {
        var expected = new List<QueryRow>();
        if (state.Cancelled is not null)
        { expected.Add(Row(SqlTypedInsertRf3Literal.Cancelled, SqlTypedInsertRf3Literal.CancelJson)); }
        expected.Add(Row(SqlTypedInsertRf3Literal.One, SqlTypedInsertRf3Literal.OneJson));
        if (state.Repaired is not null)
        { expected.Add(Row(SqlTypedInsertRf3Literal.Three, SqlTypedInsertRf3Literal.ThreeJson)); }
        expected.Add(Row(SqlTypedInsertRf3Literal.Two, SqlTypedInsertRf3Literal.TwoJson));
        if (state.ColdRows >= FirstCold)
        { expected.Add(Row(SqlTypedInsertRf3Literal.FirstCold, SqlTypedInsertRf3Literal.FirstColdJson)); }
        if (state.ColdRows >= SecondCold)
        { expected.Add(Row(SqlTypedInsertRf3Literal.SecondCold, SqlTypedInsertRf3Literal.SecondColdJson)); }
        await SqlRf3Protocol.EqualAsync(global::System.Collections.Immutable.ImmutableArray.CreateRange(expected), page.Rows);
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.AccessPath).IsEqualTo(FullScan);
        // Public local cuts remain opaque; receipt evidence uses the genuine minimum-token read above.
    }
    private static QueryRow Row(string id, string json) => new(id, SqlTypedInsertRf3Literal.InitialRevision, json, false, []);
    private const string FullScan = "bounded-full-scan";
    private const string IdPath = "/@id";
    private const string AmountPath = "/amount";
    private const string ActivePath = "/active";
    private const string PricePath = "/price";
    private const string TimestampPath = "/at";
    private const int FirstCold = 1;
    private const int SecondCold = 2;
}
