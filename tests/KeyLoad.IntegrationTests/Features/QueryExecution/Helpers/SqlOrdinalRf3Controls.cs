using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOrdinalRf3Controls
{
    internal static async Task InvalidAsync(ClusterFixture fixture, RequestCqrsRf3Callers callers, SqlOffsetRf3State state,
        PostgresRf3Caller wire, CancellationToken token)
    {
        foreach (var ordinal in new[] { "0", "3", "2147483648", "-1", "1.0", "1e0" })
        {
            await RejectAsync(callers, state, wire, Prefix + ordinal + Tail,
                ErrorCode.Validation, InvalidParameter, SqlOffsetRf3Protocol.Q1, token);
            await SqlOrdinalRf3Owned.SourcesAsync(fixture, state, token);
            await SqlOrdinalRf3Pages.HealthyAsync(callers, state, wire, token);
            await SqlOrdinalRf3Owned.SourcesAsync(fixture, state, token);
        }
        await RejectAsync(callers, state, wire, "SELECT * FROM offsetleft ORDER BY 1 LIMIT 1",
            ErrorCode.UnsupportedCapability, Unsupported, SqlOffsetRf3Protocol.Q1, token);
        await SqlOrdinalRf3Owned.SourcesAsync(fixture, state, token);
        var joined = SqlOffsetRf3Protocol.JoinSql.Replace("ORDER BY l.order_id", "ORDER BY 2", StringComparison.Ordinal);
        await RejectAsync(callers, state, wire, joined, ErrorCode.Validation, InvalidParameter, SqlOffsetRf3Protocol.Q2, token);
        await SqlOrdinalRf3Owned.SourcesAsync(fixture, state, token);
        await SqlOrdinalRf3Pages.HealthyAsync(callers, state, wire, token);
        await SqlOrdinalRf3Owned.SourcesAsync(fixture, state, token);
    }
    internal static async Task DeniedAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state,
        PostgresRf3Caller wire, CancellationToken token)
    {
        await RejectAsync(callers, state, wire, Prefix + "2" + Tail,
            ErrorCode.PermissionDenied, InsufficientPrivilege, SqlOffsetRf3Protocol.Q1, token);
        var ast = new global::KeyLoad.Query.AstQueryRequest(state.Partition,
            new(SqlOffsetRf3Protocol.Left, null, [new("/title", "title"), new("/order_id", "order_id")],
                null, [new("/order_id", false)], SqlOffsetRf3Protocol.PageSize, Offset: SqlOffsetRf3Protocol.InitialOffset),
            AllowFullScan: true);
        var result = await callers.Sdk.QueryAstAsync(ast, token);
        await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token),
            ErrorCode.PermissionDenied, dispatched: true);
    }
    private static async Task RejectAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state,
        PostgresRf3Caller wire, string sql, ErrorCode code, string sqlState, int dialect, CancellationToken token)
    {
        var request = new QueryRequest(state.Partition, sql, AllowFullScan: true, QueryDialectVersion: dialect);
        var result = await callers.Sdk.QueryAsync(request, token);
        await global::TUnit.Assertions.Assert.That(result.IsSuccess).IsFalse();
        await global::TUnit.Assertions.Assert.That(result.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, request, token),
            code, dispatched: true);
        if (dialect != SqlOffsetRf3Protocol.Q1)
        { return; }
        foreach (var prepared in new[] { false, true })
        {
            var error = await global::TUnit.Assertions.Assert.ThrowsAsync<global::Npgsql.PostgresException>(
                () => wire.ExecuteAsync<QueryPage>(sql, [], prepared, token));
            await global::TUnit.Assertions.Assert.That(error?.SqlState).IsEqualTo(sqlState);
        }
    }
    internal static async Task CancelledAsync(ClusterFixture fixture, RequestCqrsRf3Callers callers, SqlOffsetRf3State state,
        PostgresRf3Caller wire, CancellationToken token)
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
        await SqlOrdinalRf3Owned.SourcesAsync(fixture, state, token);
        await SqlOrdinalRf3Pages.HealthyAsync(callers, state, wire, token);
        await SqlOrdinalRf3Owned.SourcesAsync(fixture, state, token);
    }
    private const string Prefix = "SELECT title, order_id FROM offsetleft ORDER BY ";
    private const string Tail = " LIMIT 1 OFFSET 1";
    private const string InvalidParameter = "22023", Unsupported = "0A000", InsufficientPrivilege = "42501";
}
