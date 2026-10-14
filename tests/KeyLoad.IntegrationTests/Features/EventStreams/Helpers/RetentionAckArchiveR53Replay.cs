using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class RetentionAckArchiveR53Replay
{
    internal static async Task SourceAsync(RequestCqrsRf3Callers callers, RetentionAckArchiveR53State state,
        CancellationToken token)
    {
        await RetentionAckArchiveR53Actions.RefusedAsync(callers, state, false, token);
        await SqlRf3Protocol.EqualAsync(state.Configured, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ConfigureSubscriptionAsync(state.Configure, token)));
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.Configured, (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, state.Configure, token))).Value);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        foreach (var (request, receipt) in state.Acks)
        {
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.CompleteSubscriptionAsync(request, token)));
            await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsComplete, request, token))).Value);
            await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        }
        foreach (var (request, result) in state.Pauses)
        {
            await SqlRf3Protocol.EqualAsync(result, await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.SetSubscriptionPausedAsync(request, token)));
            await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
            await SqlRf3Protocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
                await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsPause, request, token))).Value);
            await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        }
        foreach (var (request, receipt) in new[]
            { (state.Original.PurgedPublish, state.Original.PurgedPublishReceipt), (state.Original.Purge, state.Original.PurgeReceipt) })
        {
            await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(request, token)));
            await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
            await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, request, token))).Value);
            await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        }
    }
}
