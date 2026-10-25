using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using ManagedCode.Communication;
using P = KeyLoad.ClusterTests.DatabaseComposition.FunctionalGateAnalyticsMixedPublicProtocol;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsMixedPublicBounds
{
    internal static async Task RunAsync(FunctionalGateContext context, FunctionalGateAnalyticsMixedPublicState state,
        CommandRequest original, CommitReceipt receipt, CancellationToken token)
    {
        // Prior model workers delete their edges. This final case retains exactly one edge per writer/store pair.
        var before = await context.SdkAsync(context.Administrator.Sdk.OutboxStatusAsync(context.Partition, token));
        var request = new TraverseRequest(context.Partition, P.Graph, context.Bind(context.Anchor), P.Created,
            P.MaximumVertices, P.MaximumEdges - P.Created, [state.Label]);
        await SdkAsync(await context.Reader.Sdk.TraverseAsync(request, token));
        await HealthyAsync(context, state, original, receipt, before, token);
        _ = (await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync(
            McpCallerTools.GraphTraverse, request, token), ErrorCode.BudgetExceeded, dispatched: true)).Value;
        await HealthyAsync(context, state, original, receipt, before, token);
        var sql = SqlClusterProtocol.Call(context.Partition, McpCallerTools.GraphTraverse, request);
        await SqlSdkAsync(await context.Reader.Sdk.ExecuteSqlAsync(sql, token));
        await HealthyAsync(context, state, original, receipt, before, token);
        _ = (await McpCallerAssertions.ErrorAsync(await context.Reader.Mcp.CallAsync(
            SqlOperationProtocol.ToolName, sql, token), ErrorCode.BudgetExceeded, dispatched: true)).Value;
        await HealthyAsync(context, state, original, receipt, before, token);
    }

    private static async Task HealthyAsync(FunctionalGateContext context, FunctionalGateAnalyticsMixedPublicState state,
        CommandRequest original, CommitReceipt receipt, OutboxStatus before, CancellationToken token)
    {
        // Original exact filtered graph still admits two vertices and one edge while examining all twelve edges.
        await FunctionalGateAnalyticsMixedPublicReads.VerifyAsync(context, state, present: true, receipt.Token.Position, token);
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(receipt,
            await FunctionalGateAnalyticsMixedPublicCalls.CommitAsync(context, state, original, token));
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(before,
            await context.SdkAsync(context.Administrator.Sdk.OutboxStatusAsync(context.Partition, token)));
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(before,
            await context.McpAsync<OutboxStatus>(context.Administrator, McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(context.Partition), token));
    }

    private static async Task SdkAsync(Result<global::KeyLoad.GraphTraversal> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await ProblemAsync(result.Problem?.ErrorCode, result.Problem?.StatusCode);
    }

    private static async Task SqlSdkAsync(Result<JsonElement> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await ProblemAsync(result.Problem?.ErrorCode, result.Problem?.StatusCode);
    }

    private static async Task ProblemAsync(string? code, int? status)
    {
        await Assert.That(code).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        await Assert.That(status).IsEqualTo(Errors.Status(ErrorCode.BudgetExceeded));
    }
}
