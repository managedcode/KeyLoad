using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class SqlOffsetRf3Refusals
{
    internal static async Task InvalidAsync(RequestCqrsRf3Callers callers, SqlOffsetRf3State state, CancellationToken token)
    {
        var original = SqlOffsetRf3Protocol.JoinRequest(state.Partition);
        foreach (var (tail, code) in new[]
        {
            ("LIMIT 1 OFFSET -1", ErrorCode.Validation),
            ("LIMIT 1 OFFSET 2147483647", ErrorCode.Validation),
            ("LIMIT 1 OFFSET 1 OFFSET 1", ErrorCode.UnsupportedCapability),
            ("LIMIT 1 OFFSET @skip", ErrorCode.Validation)
        })
        {
            var bad = original with { Sql = original.Sql.Replace("LIMIT 1 OFFSET 1", tail, StringComparison.Ordinal) };
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
        foreach (var query in new[] { SqlOffsetRf3Protocol.ScalarRequest(state.Partition), SqlOffsetRf3Protocol.JoinRequest(state.Partition) })
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
}
