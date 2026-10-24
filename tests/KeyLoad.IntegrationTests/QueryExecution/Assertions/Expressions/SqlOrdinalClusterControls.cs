using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlOrdinalClusterControls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, RequestCqrsClusterCallers callers, SqlOffsetClusterState state,
        PostgresClusterCaller wire, CancellationToken token)
    {
        foreach (var ordinal in new[] { "0", "3", "2147483648", "-1", "1.0", "1e0" })
        {
            await RejectAsync(callers, state, wire, Prefix + ordinal + Tail,
                ErrorCode.Validation, InvalidParameter, SqlOffsetClusterProtocol.Q1, token);
            await SqlOrdinalClusterOwned.SourcesAsync(fixture, state, token);
            await SqlOrdinalClusterPages.HealthyAsync(callers, state, wire, token);
            await SqlOrdinalClusterOwned.SourcesAsync(fixture, state, token);
        }
        await RejectAsync(callers, state, wire, "SELECT TOP (1) * FROM offsetleft ORDER BY 1",
            ErrorCode.UnsupportedCapability, Unsupported, SqlOffsetClusterProtocol.Q1, token);
        await SqlOrdinalClusterOwned.SourcesAsync(fixture, state, token);
        var joined = JoinedOrdinal;
        await RejectAsync(callers, state, wire, joined, ErrorCode.Validation, InvalidParameter, SqlOffsetClusterProtocol.Q2, token);
        await SqlOrdinalClusterOwned.SourcesAsync(fixture, state, token);
        await SqlOrdinalClusterPages.HealthyAsync(callers, state, wire, token);
        await SqlOrdinalClusterOwned.SourcesAsync(fixture, state, token);
    }
    internal static async Task DeniedAsync(RequestCqrsClusterCallers callers, SqlOffsetClusterState state,
        PostgresClusterCaller wire, CancellationToken token)
    {
        await RejectAsync(callers, state, wire, Prefix + "2" + Tail,
            ErrorCode.PermissionDenied, InsufficientPrivilege, SqlOffsetClusterProtocol.Q1, token);
        var ast = new global::KeyLoad.Query.AstQueryRequest(state.Partition,
            new(SqlOffsetClusterProtocol.Left, null, [new("/title", "title"), new("/order_id", "order_id")],
                null, [new("/order_id", false)], SqlOffsetClusterProtocol.PageSize, Offset: SqlOffsetClusterProtocol.InitialOffset),
            AllowFullScan: true);
        var result = await callers.Sdk.QueryAstAsync(ast, token);
        await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token),
            ErrorCode.PermissionDenied, dispatched: true);
    }
    private static async Task RejectAsync(RequestCqrsClusterCallers callers, SqlOffsetClusterState state,
        PostgresClusterCaller wire, string sql, ErrorCode code, string sqlState, int dialect, CancellationToken token)
    {
        var request = new QueryRequest(state.Partition, sql, AllowFullScan: true, QueryDialectVersion: dialect);
        var result = await callers.Sdk.QueryAsync(request, token);
        await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token),
            code, dispatched: true);
        if (dialect != SqlOffsetClusterProtocol.Q1)
        {
            return;
        }
        foreach (var prepared in new[] { false, true })
        {
            var error = await global::TUnit.Assertions.Assert.ThrowsAsync<global::Npgsql.PostgresException>(
                () => wire.ExecuteAsync<QueryPage>(sql, [], prepared, token));
            await global::TUnit.Assertions.Assert.That(error?.SqlState).IsEqualTo(sqlState);
        }
    }
    internal static async Task CancelledAsync(ClusterFixture fixture, RequestCqrsClusterCallers callers, SqlOffsetClusterState state,
        PostgresClusterCaller wire, CancellationToken token)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var original = cancelled.Token;
        var request = new QueryRequest(state.Partition, Prefix + "2" + Tail, AllowFullScan: true);
        var sdk = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
            () => callers.Sdk.QueryAsync(request, original));
        await global::TUnit.Assertions.Assert.That(sdk?.CancellationToken).IsEqualTo(original);
        var mcp = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
            () => callers.Mcp.CallAsync(McpCallerTools.QueryExecute, request, original));
        await global::TUnit.Assertions.Assert.That(mcp?.CancellationToken).IsEqualTo(original);
        var driver = await global::TUnit.Assertions.Assert.ThrowsAsync<OperationCanceledException>(
            () => wire.ExecuteAsync<QueryPage>(request.Sql, [], prepared: false, original));
        await global::TUnit.Assertions.Assert.That(driver?.CancellationToken).IsEqualTo(original);
        await SqlOrdinalClusterOwned.SourcesAsync(fixture, state, token);
        await SqlOrdinalClusterPages.HealthyAsync(callers, state, wire, token);
        await SqlOrdinalClusterOwned.SourcesAsync(fixture, state, token);
    }
    private const string JoinedOrdinal = "SELECT l.title AS title,r.name AS name FROM offsetleft AS l INNER JOIN offsetright AS r ON l.customer_id=r.id ORDER BY 2 OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY";
    private const string Prefix = "SELECT title, order_id FROM offsetleft ORDER BY ";
    private const string Tail = " OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY";
    private const string InvalidParameter = "22023", Unsupported = "0A000", InsufficientPrivilege = "42501";
}
