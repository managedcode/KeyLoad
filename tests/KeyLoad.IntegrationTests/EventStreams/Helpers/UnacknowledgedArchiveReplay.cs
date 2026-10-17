using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveReplay
{
    internal static async Task SuccessAsync<T>(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        string tool, object request, T expected, Func<Task<ManagedCode.Communication.Result<T>>> sdk,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await sdk()));
        await UnacknowledgedArchiveCurrent.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<T>(
            await callers.Mcp.CallAsync(tool, request, token))).Value);
        await UnacknowledgedArchiveCurrent.RunAsync(callers, state, token);
    }

    internal static async Task AllAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        foreach (var entry in state.Replays)
        {
            switch (entry.Request)
            {
                case CommandRequest command:
                    await SuccessAsync(callers, state, entry.Tool, command, (CommitReceipt)entry.Result,
                        () => callers.Sdk.CommitAsync(command, token), token);
                    break;
                case ConfigureSubscriptionRequest configure:
                    await SuccessAsync(callers, state, entry.Tool, configure, (SubscriptionInfo)entry.Result,
                        () => callers.Sdk.ConfigureSubscriptionAsync(configure, token), token);
                    break;
                case SetSubscriptionPausedRequest pause:
                    await SuccessAsync(callers, state, entry.Tool, pause, (SubscriptionInfo)entry.Result,
                        () => callers.Sdk.SetSubscriptionPausedAsync(pause, token), token);
                    break;
                case SeekSubscriptionRequest seek:
                    await SuccessAsync(callers, state, entry.Tool, seek, (SubscriptionInfo)entry.Result,
                        () => callers.Sdk.SeekSubscriptionAsync(seek, token), token);
                    break;
                case SubscriptionDeliveryCommand delivery:
                    await SuccessAsync(callers, state, entry.Tool, delivery, (CommitReceipt)entry.Result,
                        () => callers.Sdk.CompleteSubscriptionAsync(delivery, token), token);
                    break;
                default:
                    throw new InvalidOperationException(UnacknowledgedArchiveProtocol.Missing);
            }
        }
        if (state.Checkpoint == state.Issued && state.Issued > UnacknowledgedArchiveProtocol.BeforeFirst)
        {
            await UnacknowledgedArchiveFailures.SettledReceiveAsync(callers, state, token);
        }
        var failures = state.Restored ? state.CurrentFailures : state.SourceFailures;
        foreach (var command in failures)
        {
            await UnacknowledgedArchiveActions.RefusedAsync(callers, state, command, token);
        }
    }
}
