using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationHistory
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        await CommitAsync(root, state, state.Scenario.Original, state.Scenario.Receipt, token);
        foreach (var retained in state.Receipts)
        {
            await CompareAsync(root, state, retained.Receipt, async () => await McpCallerAssertions.SdkSuccessAsync(
                await root.Sdk.CompleteAsync(retained.Command, token)), token);
            await CompareAsync(root, state, retained.Receipt, () => SqlClusterProtocol.McpAsync<CommitReceipt>(root.Mcp,
                SqlClusterProtocol.Call(state.Scenario.Partition, McpCallerTools.MessagesComplete, retained.Command), token), token);
        }
        var request = state.Claim ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent);
        await QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
            await QueueViewLeaseIsolationProblems.SdkAsync(await root.Sdk.ReceiveAsync(request, token), ErrorCode.StaleLease), token);
        await QueueViewLeaseIsolationImages.ObserveAsync(root, state, async () =>
            await QueueViewLeaseIsolationProblems.McpAsync(await root.Mcp.CallAsync(McpCallerTools.MessagesReceive,
                request, token), state, ErrorCode.StaleLease), token);
        if (state.HealthyCommand is { } healthy && state.HealthyReceipt is { } receipt)
        {
            await CommitAsync(root, state, healthy, receipt, token);
        }
    }

    private static async Task CommitAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state,
        CommandRequest command, CommitReceipt receipt, CancellationToken token)
    {
        await CompareAsync(root, state, receipt, async () => await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.CommitAsync(command, token)), token);
        await CompareAsync(root, state, receipt, async () => (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await root.Mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value, token);
        await CompareAsync(root, state, receipt, () => SqlClusterProtocol.SdkAsync<CommitReceipt>(root.Sdk,
            SqlClusterProtocol.Call(state.Scenario.Partition, McpCallerTools.DocumentsCommit, command), token), token);
        await CompareAsync(root, state, receipt, () => SqlClusterProtocol.McpAsync<CommitReceipt>(root.Mcp,
            SqlClusterProtocol.Call(state.Scenario.Partition, McpCallerTools.DocumentsCommit, command), token), token);
    }

    private static Task CompareAsync<T>(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state,
        T expected, Func<Task<T>> operation, CancellationToken token)
        => QueueViewLeaseIsolationImages.ObserveAsync(root, state,
            async () => await SqlClusterProtocol.EqualAsync(expected, await operation()), token);
}
