using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueParkedPolicyClusterReplay
{
    internal static async Task CommandAsync(RequestCqrsClusterCallers worker, CommandRequest request,
        CommitReceipt original, Func<Task> current, CancellationToken token)
    {
        await QueueParkedPolicyClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.CommitAsync(request, token)), original);
        await current();
        await QueueParkedPolicyClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token))).Value, original);
        await current();
    }

    internal static async Task CompletionAsync(RequestCqrsClusterCallers worker, DeliveryCommand request,
        CommitReceipt original, Func<Task> current, CancellationToken token)
    {
        await QueueParkedPolicyClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.CompleteAsync(request, token)), original);
        await current();
        await QueueParkedPolicyClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, request, token))).Value, original);
        await current();
    }

    internal static async Task ClaimAsync(RequestCqrsClusterCallers worker, ReceiveRequest request,
        ReceiveResult original, Func<Task> current, CancellationToken token)
    {
        await QueueParkedPolicyClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.ReceiveAsync(request, token)), original);
        await current();
        await QueueParkedPolicyClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value, original);
        await current();
    }

    internal static async Task AllAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        Func<Task> current = () => QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        foreach (var row in state.Commands)
        {
            await CommandAsync(worker, row.Request, row.Result, current, token);
        }
        foreach (var row in state.Completions)
        {
            await CompletionAsync(worker, row.Request, row.Result, current, token);
        }
        foreach (var row in state.Configurations)
        {
            await QueueParkedPolicyClusterPolicy.ReplayAsync(admin, row.Id, row.Request, row.Result, current, token);
        }
    }
}
