using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveActions
{
    internal static async Task AckAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        int index, long checkpoint, CancellationToken token)
    {
        var delivery = state.Received.Deliveries[index];
        var command = new SubscriptionDeliveryCommand(Guid.NewGuid(), state.Group, delivery.Token, DeliveryAction.Ack);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteSubscriptionAsync(command, token));
        await UnacknowledgedArchiveCurrent.ReceiptAsync(state, result, command.CommandId,
            new(UnacknowledgedArchiveProtocol.AckKind, state.Source.Resource,
                delivery.Event.Position.ToString(System.Globalization.CultureInfo.InvariantCulture), UnacknowledgedArchiveProtocol.OriginalLeaseVersion));
        state.Checkpoint = checkpoint;
        await ChangedAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.SuccessAsync(callers, state, McpCallerTools.SubscriptionsComplete,
            command, result, () => callers.Sdk.CompleteSubscriptionAsync(command, token), token);
        state.Replays.Add((McpCallerTools.SubscriptionsComplete, command, result));
    }

    internal static async Task PauseAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        bool paused, CancellationToken token)
    {
        var request = new SetSubscriptionPausedRequest(Guid.NewGuid(), state.Group, state.Generation, paused);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.SetSubscriptionPausedAsync(request, token));
        state.Paused = paused;
        await SqlRf3Protocol.EqualAsync(UnacknowledgedArchiveCurrent.Expected(state), result);
        await ChangedAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.SuccessAsync(callers, state, McpCallerTools.SubscriptionsPause,
            request, result, () => callers.Sdk.SetSubscriptionPausedAsync(request, token), token);
        state.Replays.Add((McpCallerTools.SubscriptionsPause, request, result));
    }

    internal static Task ChangedAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
        => ChangedCoreAsync(callers, state, token);

    private static async Task ChangedCoreAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        await UnacknowledgedArchiveCurrent.CaptureAsync(callers, state, token);
        await UnacknowledgedArchiveCurrent.RunAsync(callers, state, token);
    }

    internal static CommandRequest Refusal(UnacknowledgedArchiveState state)
        => new(Guid.NewGuid(), state.Original.Partition, [new PurgeTopic(state.Source.Resource, UnacknowledgedArchiveProtocol.PurgeThrough)]);

    internal static Task RefusedAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CommandRequest command, CancellationToken token)
        => UnacknowledgedArchiveFailures.RequireAsync(callers, state, McpCallerTools.DocumentsCommit, command,
            () => callers.Sdk.CommitAsync(command, token), ErrorCode.ResourceExhausted,
            UnacknowledgedArchiveProtocol.PinDetail, UnacknowledgedArchiveProtocol.SafeCapacity, token);
}
