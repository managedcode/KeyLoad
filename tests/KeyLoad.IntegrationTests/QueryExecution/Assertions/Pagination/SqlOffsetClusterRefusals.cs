using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlOffsetClusterRefusals
{
    internal static async Task InvalidAsync(RequestCqrsClusterCallers callers, SqlOffsetClusterState state, CancellationToken token)
    {
        var original = SqlOffsetClusterProtocol.JoinRequest(state.Partition);
        await Assert.That(original.Sql.EndsWith(RuntimeWindow, StringComparison.Ordinal)).IsTrue();
        var ordered = original.Sql[..^RuntimeWindow.Length];
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
            var bad = original with
            {
                Sql = sql
            };
            var failure = await callers.Sdk.QueryAsync(bad, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(code.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, bad, token),
                code, dispatched: true);
        }
        var ast = SqlOffsetClusterProtocol.Join(state.Partition);
        foreach (var offset in new[] { -1, int.MaxValue })
        {
            var bad = ast with
            {
                Query = ast.Query with
                {
                    Offset = offset
                }
            };
            // A negative offset or an offset whose window passes the 32-bit row position is invalid input, as in SQL.
            var failure = await callers.Sdk.QueryAstAsync(bad, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.Validation.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryAst, bad, token),
                ErrorCode.Validation, dispatched: true);
        }
    }
    internal static async Task DeniedAsync(RequestCqrsClusterCallers callers, SqlOffsetClusterState state, CancellationToken token)
    {
        var plain = SqlOffsetClusterProtocol.JoinRequest(state.Partition);
        foreach (var query in new[] { SqlOffsetClusterProtocol.ScalarRequest(state.Partition), plain,
            plain with { Sql = plain.Sql.Replace(" INNER JOIN ", " JOIN ", StringComparison.Ordinal) } })
        {
            var failure = await callers.Sdk.QueryAsync(query, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryExecute, query, token),
                ErrorCode.PermissionDenied, dispatched: true);
        }
        foreach (var query in new[] { SqlOffsetClusterProtocol.Scalar(state.Partition), SqlOffsetClusterProtocol.Join(state.Partition) })
        {
            var failure = await callers.Sdk.QueryAstAsync(query, token);
            await Assert.That(failure.IsSuccess).IsFalse();
            await Assert.That(failure.Problem?.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.QueryAst, query, token),
                ErrorCode.PermissionDenied, dispatched: true);
        }
    }
    private const string RuntimeWindow = "OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY";

}
