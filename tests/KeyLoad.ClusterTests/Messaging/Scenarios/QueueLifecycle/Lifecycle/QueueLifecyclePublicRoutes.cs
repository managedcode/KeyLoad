using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueLifecyclePublicRoutes
{
    internal static async Task<CommitReceipt> CommitAsync(RequestCqrsClusterCallers callers, QueueLifecyclePublicState state,
        CommandRequest command, CancellationToken token)
    {
        var receipt = await CallAsync(callers, state.Route, state.Partition, McpCallerTools.DocumentsCommit, command,
            () => callers.Sdk.CommitAsync(command, token), token);
        state.Commands.Add((command, receipt));
        return receipt;
    }

    internal static async Task<T> CallAsync<T>(RequestCqrsClusterCallers callers, int route, PartitionRef partition,
        string tool, object request, Func<Task<ManagedCode.Communication.Result<T>>> sdk, CancellationToken token)
        => route switch
        {
            QueueLifecyclePublicProtocol.Sdk => await McpCallerAssertions.SdkSuccessAsync(await sdk()),
            QueueLifecyclePublicProtocol.Mcp => (await McpCallerAssertions.SuccessAsync<T>(await callers.Mcp.CallAsync(tool, request, token))).Value,
            QueueLifecyclePublicProtocol.Q1Sdk => await SqlClusterProtocol.SdkAsync<T>(callers.Sdk, SqlClusterProtocol.Call(partition, tool, request), token),
            QueueLifecyclePublicProtocol.Q1Mcp => await SqlClusterProtocol.McpAsync<T>(callers.Mcp, SqlClusterProtocol.Call(partition, tool, request), token),
            _ => throw new InvalidOperationException("Unknown fixture public route.")
        };

    internal static async Task RefusedAsync(RequestCqrsClusterCallers callers, QueueLifecyclePublicState state,
        CommandRequest command, ErrorCode code, CancellationToken token)
    {
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await callers.Sdk.CommitAsync(command, token), code);
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token), code, dispatched: true)).Value;
        var sql = SqlClusterProtocol.Call(state.Partition, McpCallerTools.DocumentsCommit, command);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), code);
        _ = (await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), code, dispatched: true)).Value;
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers callers, QueueLifecyclePublicState state, CancellationToken token)
    {
        foreach (var route in QueueLifecyclePublicProtocol.Routes)
        {
            foreach (var (command, receipt) in state.Commands)
            {
                await SameAsync(receipt, await CallAsync(callers, route, state.Partition, McpCallerTools.DocumentsCommit,
                command, () => callers.Sdk.CommitAsync(command, token), token));
            }
            foreach (var (command, receipt) in state.Deliveries)
            {
                await SameAsync(receipt, await CallAsync(callers, route, state.Partition, McpCallerTools.MessagesComplete,
                command, () => callers.Sdk.CompleteAsync(command, token), token));
            }
        }
    }

    private static async Task SameAsync(CommitReceipt expected, CommitReceipt actual)
        => await Assert.That(OrleansSerialization.Serialize(actual).SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
}
