using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsMixedPublicCalls
{
    internal static async Task<T[]> ReadAsync<T>(FunctionalGateContext context, string tool, object request,
        Task<Result<T>> original, CancellationToken token) where T : class
    {
        var sdk = await context.SdkAsync(original);
        var mcp = await context.McpAsync<T>(context.Reader, tool, request, token);
        var sql = SqlClusterProtocol.Call(context.Partition, tool, request);
        var sqlSdk = await FunctionalGateCompositionCalls.SqlAsync<T>(context, sql, official: false, write: false, token);
        var sqlMcp = await FunctionalGateCompositionCalls.SqlAsync<T>(context, sql, official: true, write: false, token);
        return [sdk, mcp, sqlSdk, sqlMcp];
    }

    internal static async Task<CommitReceipt> CommitAsync(FunctionalGateContext context,
        FunctionalGateAnalyticsMixedPublicState state, CommandRequest command, CancellationToken token)
    {
        var original = await context.SdkAsync(context.Writer.Sdk.CommitAsync(command, token));
        await FunctionalGateCompositionCalls.ReceiptAsync(context, original, command.CommandId, state.Effects);
        await ReceiptAsync(context, state, original, await context.McpAsync<CommitReceipt>(context.Writer,
            McpCallerTools.DocumentsCommit, FunctionalGateAnalyticsIdentity.Wire(command), token));
        await ReceiptAsync(context, state, original, await FunctionalGateCompositionCalls.CommandAsync(
            context, FunctionalGateAnalyticsIdentity.Rebuild(command), official: false, token));
        await ReceiptAsync(context, state, original, await FunctionalGateCompositionCalls.CommandAsync(
            context, FunctionalGateAnalyticsIdentity.Rebuild(command), official: true, token));
        return original;
    }

    internal static async Task RefuseAsync(FunctionalGateContext context, CommandRequest command,
        Func<Task> unchanged, CancellationToken token)
    {
        var sdk = await context.Writer.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await ProblemAsync(sdk.Problem?.ErrorCode, sdk.Problem?.StatusCode);
        await unchanged();
        _ = (await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(
            McpCallerTools.DocumentsCommit, FunctionalGateAnalyticsIdentity.Wire(command), token), ErrorCode.RevisionConflict, dispatched: true)).Value;
        await unchanged();
        var sql = FunctionalGateRelationalCalls.Commit(command);
        var sqlSdk = await context.Writer.Sdk.ExecuteSqlAsync(sql, token);
        await Assert.That(sqlSdk.IsFailed).IsTrue();
        await Assert.That(sqlSdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await ProblemAsync(sqlSdk.Problem?.ErrorCode, sqlSdk.Problem?.StatusCode);
        await unchanged();
        _ = (await McpCallerAssertions.ErrorAsync(await context.Writer.Mcp.CallAsync(
            SqlOperationProtocol.ToolName, sql, token), ErrorCode.RevisionConflict, dispatched: true)).Value;
        await unchanged();
    }

    private static async Task ReceiptAsync(FunctionalGateContext context, FunctionalGateAnalyticsMixedPublicState state,
        CommitReceipt expected, CommitReceipt actual)
    {
        await FunctionalGateUserAnalyticsAssertions.EqualAsync(actual, expected);
        await FunctionalGateCompositionCalls.ReceiptAsync(context, actual, expected.CommandId, state.Effects);
    }

    private static async Task ProblemAsync(string? code, int? status)
    {
        await Assert.That(code).IsEqualTo(ErrorCode.RevisionConflict.ToString());
        await Assert.That(status).IsEqualTo(Errors.Status(ErrorCode.RevisionConflict));
    }
}
