using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.QueryExecution;

internal static class QueryRuleOptimizationClusterCancellation
{
    private const string ReadUnavailable = "The read response is unavailable.";

    internal static async Task RunAsync(RequestCqrsClusterCallers reader, QueryRuleOptimizationClusterState state)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var token = cancellation.Token;
        var query = QueryRuleOptimizationClusterRequests.Sql(state.Partition, QueryRuleOptimizationClusterData.RevisionOnlyCollection, QueryRuleOptimizationClusterData.OpenRevisionOneShape, false);
        var ast = QueryRuleOptimizationClusterRequests.Builder(state.Partition, QueryRuleOptimizationClusterData.RevisionOnlyCollection, QueryRuleOptimizationClusterData.OpenRevisionOneShape, false);
        var original = reader.Sdk.QueryAstAsync(ast, token);
        var result = await original;
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, ReadUnavailable), result.Problem);
        var originalSql = reader.Sdk.ExecuteSqlAsync(new(state.Partition, query.Sql, AllowFullScan: true), token);
        var sql = await originalSql;
        await Assert.That(sql.IsFailed).IsTrue();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, ReadUnavailable), sql.Problem);
        var official = reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () => _ = await official);
        await Assert.That(failure!.CancellationToken).IsEqualTo(token);
    }
}
