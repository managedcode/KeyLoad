using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOffsetRf3Refusals
{
    internal static async Task InvalidAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state, CancellationToken token)
    {
        var original = SqlOffsetRf3Protocol.JoinRequest(state.Partition);
        await Assert.That(original.Sql.EndsWith(NativeWindow, StringComparison.Ordinal)).IsTrue();
        var ordered = original.Sql[..^NativeWindow.Length];
        foreach (var (sql, code) in new[]
        {
            (ordered + "OFFSET -1 ROWS FETCH NEXT 1 ROWS ONLY", ErrorCode.Validation),
            (ordered + "OFFSET 2147483647 ROWS FETCH NEXT 1 ROWS ONLY", ErrorCode.Validation),
            (ordered.Replace("SELECT ", "SELECT TOP (1) PERCENT ", StringComparison.Ordinal), ErrorCode.UnsupportedCapability),
            (ordered + "OFFSET @skip ROWS FETCH NEXT 1 ROWS ONLY", ErrorCode.Validation),
            (ordered + "OFFSET -1 ROWS FETCH FIRST 1 ROW ONLY", ErrorCode.Validation),
            (ordered + "OFFSET 2147483647 ROWS FETCH NEXT 1 ROW ONLY", ErrorCode.Validation),
            (ordered + "OFFSET 1 ROWS FETCH NEXT 0 ROWS ONLY", ErrorCode.Validation),
            (ordered + "OFFSET (1 + 0) ROWS FETCH NEXT 1 ROWS ONLY", ErrorCode.UnsupportedCapability),
            (ordered.Replace("SELECT ", "SELECT TOP (1) WITH TIES ", StringComparison.Ordinal), ErrorCode.UnsupportedCapability)
        })
        {
            var bad = original with { Sql = sql };
            var failure = await callers.Sdk.QueryAsync(bad, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, bad, token),
                code, dispatched: true);
        }
        var ast = SqlOffsetRf3Protocol.Join(state.Partition);
        foreach (var offset in new[] { -1, int.MaxValue })
        {
            var bad = ast with { Query = ast.Query with { Offset = offset } };
            var failure = await callers.Sdk.QueryAstAsync(bad, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.BudgetExceeded.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryAst, bad, token),
                ErrorCode.BudgetExceeded, dispatched: true);
        }
    }
    internal static async Task DeniedAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state, CancellationToken token)
    {
        var plain = SqlOffsetRf3Protocol.JoinRequest(state.Partition);
        foreach (var query in new[] { SqlOffsetRf3Protocol.ScalarRequest(state.Partition), plain,
            plain with { Sql = plain.Sql.Replace(" INNER JOIN ", " JOIN ", StringComparison.Ordinal) } })
        {
            var failure = await callers.Sdk.QueryAsync(query, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, query, token),
                ErrorCode.PermissionDenied, dispatched: true);
        }
        foreach (var query in new[] { SqlOffsetRf3Protocol.Scalar(state.Partition), SqlOffsetRf3Protocol.Join(state.Partition) })
        {
            var failure = await callers.Sdk.QueryAstAsync(query, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryAst, query, token),
                ErrorCode.PermissionDenied, dispatched: true);
        }
    }
    private const string NativeWindow = "OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY";

}
