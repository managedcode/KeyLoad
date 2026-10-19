using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyClusterReplay
{
    internal static async Task ProducerAsync(RequestCqrsClusterCallers worker, CommandRequest request,
        CommitReceipt original, Func<Task> current, CancellationToken token)
    {
        await QueueTerminalPolicyClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.CommitAsync(request, token)), original);
        await current();
        await QueueTerminalPolicyClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token))).Value, original);
        await current();
    }

    internal static async Task CompletionAsync(RequestCqrsClusterCallers worker, DeliveryCommand request,
        CommitReceipt original, Func<Task> current, CancellationToken token)
    {
        await QueueTerminalPolicyClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.CompleteAsync(request, token)), original);
        await current();
        await QueueTerminalPolicyClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, request, token))).Value, original);
        await current();
    }

    internal static async Task ClaimAsync(RequestCqrsClusterCallers worker, ReceiveRequest request,
        ReceiveResult original, Func<Task> current, CancellationToken token)
    {
        await QueueTerminalPolicyClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.ReceiveAsync(request, token)), original);
        await current();
        await QueueTerminalPolicyClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value, original);
        await current();
    }

    internal static async Task AllAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, CancellationToken token)
    {
        Func<Task> current = () => QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        foreach (var row in state.Producers)
        {
            await ProducerAsync(worker, row.Request, row.Result, current, token);
        }
        foreach (var row in state.Completions)
        {
            await CompletionAsync(worker, row.Request, row.Result, current, token);
        }
        foreach (var row in state.Configurations)
        {
            await QueueTerminalPolicyClusterPolicy.ReplayAsync(admin, row.Id, row.Request, row.Result, current, token);
        }
    }
}
