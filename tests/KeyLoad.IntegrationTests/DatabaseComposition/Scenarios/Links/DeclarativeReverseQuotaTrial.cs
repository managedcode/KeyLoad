using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal static class DeclarativeReverseQuotaTrial
{
    internal const string HolderId = "quota-holder";
    internal const string Payload = "{\"holder\":true}";
    private const string Headers = "{}";
    private const string MissingHolder = "The original quota holder is unavailable.";
    private const string MissingClaim = "The quota holder original claim is unavailable.";

    internal static async Task<DeclarativeReverseQuotaState> ReleaseAsync(RequestCqrsClusterCallers callers,
        DeclarativeReverseQuotaState state, CancellationToken token)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), new(state.Scenario.Partition, DeclarativeReverseQuotaFlow.Target),
            MaxMessages: 1, LeaseSeconds: new QueuePolicy().MaxLeaseSeconds);
        var claim = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        await Assert.That(claim.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(claim.Deliveries).HasSingleItem();
        await Assert.That(delivery.Id).IsEqualTo(HolderId);
        await Assert.That(delivery.PayloadJson).IsEqualTo(Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(Headers);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        await Assert.That(delivery.Token).IsNotEmpty();
        var inspection = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(new(request.Lane, HolderId), token))
            ?? throw new InvalidOperationException(MissingHolder);
        await Assert.That(inspection.Metadata.LeaseOwner).IsNotNull();
        var leased = new MessageInspection(new(HolderId, MessageState.Leased, 1, 2L, 1L, null, null,
            inspection.Metadata.LeaseOwner, 1L, delivery.LeaseUntil), Payload, Headers);
        await MessageAsync(callers, request.Lane, leased, token);
        await SqlClusterProtocol.EqualAsync(claim,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(claim, (await McpCallerAssertions.SuccessAsync<ReceiveResult>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value);
        var ack = new DeliveryCommand(Guid.NewGuid(), request.Lane, delivery.Token, DeliveryAction.Ack);
        var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token))).Value;
        await ReceiptAsync(state.Holder, state.HolderReceipt, "enqueue", 1L);
        await ReceiptAsync(new(ack.CommandId, request.Lane.Partition, []), receipt, "Ack", 3L);
        return state with
        {
            ClaimRequest = request,
            Claim = claim,
            ClaimImage = global::System.Text.Json.JsonSerializer.SerializeToElement(claim, JsonDefaults.Options),
            HolderAck = ack,
            HolderAckReceipt = receipt
        };
    }

    internal static async Task HistoryAsync(RequestCqrsClusterCallers callers,
        DeclarativeReverseQuotaState state, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(state.Target, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ConfigureResourceAsync(state.ConfigureId,
                new(state.Scenario.Partition.TenantId, state.Scenario.Partition.DatabaseId, state.Target), token)));
        await SqlClusterProtocol.EqualAsync(state.HolderReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.Holder, token)));
        await SqlClusterProtocol.EqualAsync(state.HolderReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.DocumentsCommit, state.Holder, token))).Value);
        var lane = new QueueLaneRef(state.Scenario.Partition, DeclarativeReverseQuotaFlow.Target);
        if (state.HolderAck is not { } ack || state.HolderAckReceipt is not { } receipt)
        {
            await MessageAsync(callers, lane, new(new(HolderId, MessageState.Ready, 0, 1L, 1L, null, null), Payload, Headers), token);
            return;
        }
        var originalClaim = state.Claim ?? throw new InvalidOperationException(MissingClaim);
        var originalRequest = state.ClaimRequest ?? throw new InvalidOperationException(MissingClaim);
        await Assert.That(originalClaim.RequestId).IsEqualTo(originalRequest.RequestId);
        await Assert.That(originalClaim.Deliveries).HasSingleItem();
        await SqlClusterProtocol.EqualAsync(state.ClaimImage ?? throw new InvalidOperationException(MissingClaim),
            global::System.Text.Json.JsonSerializer.SerializeToElement(originalClaim, JsonDefaults.Options));
        await ReceiptAsync(new(ack.CommandId, lane.Partition, []), receipt, "Ack", 3L);
        await SqlClusterProtocol.EqualAsync(receipt,
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(ack, token)));
        await SqlClusterProtocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token))).Value);
        await global::KeyLoad.IntegrationTests.Features.Messaging.SagaTimeoutClusterAssertions.AssertSdkErrorAsync(
            await callers.Sdk.ReceiveAsync(originalRequest, token), ErrorCode.StaleLease);
        await McpCallerAssertions.ErrorAsync(await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive,
            originalRequest, token), ErrorCode.StaleLease, dispatched: true);
        await MessageAsync(callers, lane, new(new(HolderId, MessageState.Acked, 1, 3L, 1L, null, null,
            LeaseVersion: 1L), null, null), token);
    }

    internal static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt, string kind, long version)
    {
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(command.CommandId, receipt.Token,
            [new(kind, DeclarativeReverseQuotaFlow.Target, HolderId, version)], DurabilityProfile.QuorumProcessDurable), receipt);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(command.Partition.AtomicPartitionId);
    }

    private static async Task MessageAsync(RequestCqrsClusterCallers callers, QueueLaneRef lane,
        MessageInspection expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(lane, HolderId);
        await SqlClusterProtocol.EqualAsync(expected,
            await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(request, token)));
        await SqlClusterProtocol.EqualAsync(expected, (await PublicReadAssertions.MessageAsync(
            await callers.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value);
    }
}
