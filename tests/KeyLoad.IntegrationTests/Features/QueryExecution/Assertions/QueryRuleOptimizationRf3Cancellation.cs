using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationRf3Cancellation
{
    private const string ReadUnavailable = "The read response is unavailable.";

    internal static async Task RunAsync(RequestCqrsRf3Callers reader, QueryRuleOptimizationRf3State state)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var token = cancellation.Token;
        var query = QueryRuleOptimizationRf3Requests.Sql(state.Partition, QueryRuleOptimizationRf3Data.RevisionOnlyCollection, QueryRuleOptimizationRf3Data.OpenRevisionOneShape, false);
        var ast = QueryRuleOptimizationRf3Requests.Builder(state.Partition, QueryRuleOptimizationRf3Data.RevisionOnlyCollection, QueryRuleOptimizationRf3Data.OpenRevisionOneShape, false);
        var original = reader.Sdk.QueryAstAsync(ast, token);
        var result = await original;
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, ReadUnavailable), result.Problem);
        var originalSql = reader.Sdk.ExecuteSqlAsync(new(state.Partition, query.Sql, AllowFullScan: true), token);
        var sql = await originalSql;
        await Assert.That(sql.IsFailed).IsTrue();
        await Assert.That(sql.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Cancelled, ReadUnavailable), sql.Problem);
        var official = reader.Mcp.CallAsync(McpCallerTools.QueryAst, ast, token);
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () => _ = await official);
        await Assert.That(failure!.CancellationToken).IsEqualTo(token);
    }
}
