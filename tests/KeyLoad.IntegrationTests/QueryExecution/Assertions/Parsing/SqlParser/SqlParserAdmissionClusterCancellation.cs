using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class SqlParserAdmissionClusterCancellation
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, SqlParserAdmissionClusterState state)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var token = cancellation.Token;
        var query = new QueryRequest(state.Partition, SqlParserAdmissionClusterReads.Plain, AllowFullScan: true);
        var original = reader.Sdk.QueryAsync(query, token);
        var result = await original;
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, "The read response is unavailable."), result.Problem);
        var originalSql = reader.Sdk.ExecuteSqlAsync(new(state.Partition, query.Sql, AllowFullScan: true), token);
        var sql = await originalSql;
        await Assert.That(sql.IsFailed).IsTrue();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, "The read response is unavailable."), sql.Problem);
        var official = reader.Mcp.CallAsync(McpCallerTools.QueryExecute, query, token);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () => _ = await official);
        await Assert.That(failure!.CancellationToken).IsEqualTo(token);
        var officialSql = reader.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            new SqlOperationRequest(state.Partition, query.Sql, AllowFullScan: true), token);
        var sqlFailure = await Assert.ThrowsAsync<OperationCanceledException>(async () => _ = await officialSql);
        await Assert.That(sqlFailure!.CancellationToken).IsEqualTo(token);
    }
}
