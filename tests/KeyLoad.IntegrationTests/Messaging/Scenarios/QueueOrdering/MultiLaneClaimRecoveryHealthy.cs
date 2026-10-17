using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class MultiLaneClaimRecoveryHealthy
{
    internal static async Task RunAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        QueueLaneRef lane, string principalId, CancellationToken token)
    {
        var id = MultiLaneClaimRecoveryProtocol.HealthyMessage;
        var payload = MultiLaneClaimRecoveryProtocol.HealthyPayload;
        var headers = MultiLaneClaimRecoveryProtocol.HealthyHeaders;
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(new(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, id, payload, headers)]), token).ConfigureAwait(false)).ConfigureAwait(false);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(), [new(Guid.NewGuid(), lane)]);
        var result = await McpCallerAssertions.SuccessAsync<MultiLaneReceiveResult>(await caller.Mcp.CallAsync(
            MultiLaneReceiveProtocol.ToolName, request, token).ConfigureAwait(false)).ConfigureAwait(false);
        var outcome = await Assert.That(result.Value.Outcomes).HasSingleItem();
        var claimed = outcome.Result!;
        await MultiLaneReceiveCancellationAssertions.EqualAsync(result.Value, new MultiLaneReceiveResult(
            request.RequestId, [new(request.Requests[MultiLaneClaimRecoveryProtocol.FirstLane].RequestId,
                lane, QueueLaneReceiveStatus.Committed, claimed, null, null)], null, null));
        var inspected = await PublicReadAssertions.MessageAsync(await administrator.Sdk.InspectAsync(
            new(lane, id), token).ConfigureAwait(false)).ConfigureAwait(false);
        var originalRequest = request.Requests[MultiLaneClaimRecoveryProtocol.FirstLane];
        await MultiLaneClaimRecoveryAuthority.ClaimAsync(administrator, originalRequest, claimed, inspected!,
            principalId, id, payload, headers, MultiLaneClaimRecoveryProtocol.HealthyReadySequence, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReceiveAcrossLanesAsync(request, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await MultiLaneReceiveCancellationAssertions.EqualAsync(replay, result.Value);
        await MultiLaneClaimRecoveryAuthority.ImageAsync(administrator, lane, id, inspected!, token);
        var delivery = claimed.Deliveries[MultiLaneClaimRecoveryProtocol.FirstLane];
        await MultiLaneReceiveCancellationAssertions.AckAsync(administrator, caller, lane, delivery,
            MultiLaneClaimRecoveryProtocol.HealthyReadySequence, token);
    }
}
