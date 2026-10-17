using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueLifecyclePublicRoutes
{
    internal static async Task<CommitReceipt> CommitAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state,
        CommandRequest command, CancellationToken token)
    {
        var receipt = await CallAsync(callers, state.Route, state.Partition, McpCallerTools.DocumentsCommit, command,
            () => callers.Sdk.CommitAsync(command, token), token);
        state.Commands.Add((command, receipt));
        return receipt;
    }

    internal static async Task<T> CallAsync<T>(RequestCqrsRf3Callers callers, int route, PartitionRef partition,
        string tool, object request, Func<Task<ManagedCode.Communication.Result<T>>> sdk, CancellationToken token)
        => route switch
        {
            QueueLifecyclePublicProtocol.Sdk => await McpCallerAssertions.SdkSuccessAsync(await sdk()),
            QueueLifecyclePublicProtocol.Mcp => (await McpCallerAssertions.SuccessAsync<T>(await callers.Mcp.CallAsync(tool, request, token))).Value,
            QueueLifecyclePublicProtocol.Q1Sdk => await SqlRf3Protocol.SdkAsync<T>(callers.Sdk, SqlRf3Protocol.Call(partition, tool, request), token),
            QueueLifecyclePublicProtocol.Q1Mcp => await SqlRf3Protocol.McpAsync<T>(callers.Mcp, SqlRf3Protocol.Call(partition, tool, request), token),
            _ => throw new InvalidOperationException("Unknown fixture public route.")
        };

    internal static async Task RefusedAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state,
        CommandRequest command, ErrorCode code, CancellationToken token)
    {
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await callers.Sdk.CommitAsync(command, token), code);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token), code, dispatched: true);
        var sql = SqlRf3Protocol.Call(state.Partition, McpCallerTools.DocumentsCommit, command);
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await callers.Sdk.ExecuteSqlAsync(sql, token), code);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(SqlOperationProtocol.ToolName, sql, token), code, dispatched: true);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers callers, QueueLifecyclePublicState state, CancellationToken token)
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
        => await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
