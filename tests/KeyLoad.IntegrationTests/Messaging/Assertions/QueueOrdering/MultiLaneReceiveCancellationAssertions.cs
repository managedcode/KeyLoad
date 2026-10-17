using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class MultiLaneReceiveCancellationAssertions
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task ReconcileAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        MultiLaneReceiveRequest request, MessageInspection committedFirst, MessageInspection beforeSecond,
        string principalId, CancellationToken token)
    {
        var first = request.Requests[MultiLaneClaimRecoveryProtocol.FirstLane];
        var recovered = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReceiveAsync(first, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await MultiLaneClaimRecoveryAuthority.ClaimAsync(administrator, first, recovered, committedFirst,
            principalId, MultiLaneReceiveRf3Flow.Message, MultiLaneReceiveRf3Flow.Payload,
            MultiLaneReceiveRf3Flow.Headers, MultiLaneClaimRecoveryProtocol.InitialReadySequence, token);
        await ImagesAsync(administrator, request, committedFirst, beforeSecond, token);
        var native = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, first, token).ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(native.Value, recovered).ConfigureAwait(false);
        await ImagesAsync(administrator, request, committedFirst, beforeSecond, token);
        var result = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReceiveAcrossLanesAsync(request, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Outcomes.Length).IsEqualTo(MultiLaneClaimRecoveryProtocol.LaneCount);
        await Assert.That(result.StopError).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        await EqualAsync(result.Outcomes[MultiLaneClaimRecoveryProtocol.FirstLane].Result, recovered).ConfigureAwait(false);
        var after = new[] { await InspectAsync(administrator, first.Lane, token),
            await InspectAsync(administrator, request.Requests[MultiLaneClaimRecoveryProtocol.SecondLane].Lane, token) };
        for (var index = MultiLaneClaimRecoveryProtocol.FirstLane; index < result.Outcomes.Length; index++)
        {
            await MultiLaneReceiveRf3Flow.CommittedAsync(administrator.Sdk, result.Outcomes[index],
                request.Requests[index], token).ConfigureAwait(false);
            await MultiLaneClaimRecoveryAuthority.ClaimAsync(administrator, request.Requests[index],
                result.Outcomes[index].Result!, after[index]!, principalId, MultiLaneReceiveRf3Flow.Message,
                MultiLaneReceiveRf3Flow.Payload, MultiLaneReceiveRf3Flow.Headers,
                MultiLaneClaimRecoveryProtocol.InitialReadySequence, token);
        }
        await ImagesAsync(administrator, request, after[MultiLaneClaimRecoveryProtocol.FirstLane]!,
            after[MultiLaneClaimRecoveryProtocol.SecondLane]!, token);
        var replay = await McpCallerAssertions.SuccessAsync<MultiLaneReceiveResult>(await caller.Mcp.CallAsync(
            MultiLaneReceiveProtocol.ToolName, request, token).ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(replay.Value, result).ConfigureAwait(false);
        await ImagesAsync(administrator, request, after[MultiLaneClaimRecoveryProtocol.FirstLane]!,
            after[MultiLaneClaimRecoveryProtocol.SecondLane]!, token);
        for (var index = MultiLaneClaimRecoveryProtocol.FirstLane; index < result.Outcomes.Length; index++)
        {
            await AckAsync(administrator, caller, request.Requests[index].Lane,
                result.Outcomes[index].Result!.Deliveries[MultiLaneClaimRecoveryProtocol.FirstLane],
                MultiLaneClaimRecoveryProtocol.InitialReadySequence, token).ConfigureAwait(false);
        }
        await MultiLaneClaimRecoveryHealthy.RunAsync(administrator, caller, first.Lane, principalId, token);
    }

    internal static async Task AckAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        QueueLaneRef lane, Delivery delivery, long readySequence, CancellationToken token)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var result = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteAsync(command, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await MultiLaneClaimRecoveryAuthority.AckAsync(administrator, command, result, delivery.Id, readySequence, token);
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, command, token).ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(replay.Value, result).ConfigureAwait(false);
        await MultiLaneClaimRecoveryAuthority.AckAsync(administrator, command, replay.Value, delivery.Id, readySequence, token);
    }

    private static async Task ImagesAsync(RequestCqrsRf3Callers administrator, MultiLaneReceiveRequest request,
        MessageInspection first, MessageInspection second, CancellationToken token)
    {
        await EqualAsync(await InspectAsync(administrator, request.Requests[MultiLaneClaimRecoveryProtocol.FirstLane].Lane, token), first);
        await EqualAsync(await InspectAsync(administrator, request.Requests[MultiLaneClaimRecoveryProtocol.SecondLane].Lane, token), second);
    }

    private static async Task<MessageInspection?> InspectAsync(RequestCqrsRf3Callers administrator,
        QueueLaneRef lane, CancellationToken token)
        => await PublicReadAssertions.MessageAsync(await administrator.Sdk.InspectAsync(
            new(lane, MultiLaneReceiveRf3Flow.Message), token).ConfigureAwait(false)).ConfigureAwait(false);
}
