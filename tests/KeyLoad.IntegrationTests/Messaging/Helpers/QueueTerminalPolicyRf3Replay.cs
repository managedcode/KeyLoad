using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyRf3Replay
{
    internal static async Task ProducerAsync(RequestCqrsRf3Callers worker, CommandRequest request,
        CommitReceipt original, Func<Task> current, CancellationToken token)
    {
        await QueueTerminalPolicyRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.CommitAsync(request, token)), original);
        await current();
        await QueueTerminalPolicyRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token))).Value, original);
        await current();
    }

    internal static async Task CompletionAsync(RequestCqrsRf3Callers worker, DeliveryCommand request,
        CommitReceipt original, Func<Task> current, CancellationToken token)
    {
        await QueueTerminalPolicyRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.CompleteAsync(request, token)), original);
        await current();
        await QueueTerminalPolicyRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesComplete, request, token))).Value, original);
        await current();
    }

    internal static async Task ClaimAsync(RequestCqrsRf3Callers worker, ReceiveRequest request,
        ReceiveResult original, Func<Task> current, CancellationToken token)
    {
        await QueueTerminalPolicyRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await worker.Sdk.ReceiveAsync(request, token)), original);
        await current();
        await QueueTerminalPolicyRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await worker.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value, original);
        await current();
    }

    internal static async Task AllAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, CancellationToken token)
    {
        Func<Task> current = () => QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
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
            await QueueTerminalPolicyRf3Policy.ReplayAsync(admin, row.Id, row.Request, row.Result, current, token);
        }
    }
}
