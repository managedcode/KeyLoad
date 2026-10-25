using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.DatabaseComposition;

internal static class FunctionalGateAnalyticsLinkRefusals
{
    internal static async Task ReadAsync(FunctionalGateContext context, ReadAnalyticsEventsRequest request,
        Func<Task> unchanged, CancellationToken token)
    {
        var sdk = await context.Administrator.Sdk.ReadAnalyticsEventsAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await ProblemAsync(sdk.Problem?.ErrorCode, sdk.Problem?.StatusCode);
        await unchanged();
        _ = (await McpCallerAssertions.ErrorAsync(await context.Administrator.Mcp.CallAsync(AnalyticsStoreProtocol.ReadTool,
            request, token), ErrorCode.OwnershipLost, dispatched: true)).Value;
        await unchanged();
        await SqlAsync(context, SqlClusterProtocol.Call(context.Partition, AnalyticsStoreProtocol.ReadTool, request), unchanged, token);
    }

    internal static async Task WriteAsync(FunctionalGateContext context, CommandRequest command,
        Func<Task> unchanged, CancellationToken token)
    {
        var sdk = await context.Administrator.Sdk.CommitAsync(command, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value).IsNull();
        await ProblemAsync(sdk.Problem?.ErrorCode, sdk.Problem?.StatusCode);
        await unchanged();
        _ = (await McpCallerAssertions.ErrorAsync(await context.Administrator.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            command, token), ErrorCode.OwnershipLost, dispatched: true)).Value;
        await unchanged();
        await SqlAsync(context, FunctionalGateRelationalCalls.Commit(command), unchanged, token);
    }

    private static async Task SqlAsync(FunctionalGateContext context, SqlOperationRequest request,
        Func<Task> unchanged, CancellationToken token)
    {
        var sdk = await context.Administrator.Sdk.ExecuteSqlAsync(request, token);
        await Assert.That(sdk.IsFailed).IsTrue();
        await Assert.That(sdk.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await ProblemAsync(sdk.Problem?.ErrorCode, sdk.Problem?.StatusCode);
        await unchanged();
        _ = (await McpCallerAssertions.ErrorAsync(await context.Administrator.Mcp.CallAsync(SqlOperationProtocol.ToolName,
            request, token), ErrorCode.OwnershipLost, dispatched: true)).Value;
        await unchanged();
    }

    private static async Task ProblemAsync(string? code, int? status)
    {
        await Assert.That(code).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        await Assert.That(status).IsEqualTo(Errors.Status(ErrorCode.OwnershipLost));
    }
}
