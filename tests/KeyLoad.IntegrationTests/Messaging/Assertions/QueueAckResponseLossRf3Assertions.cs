using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueAckResponseLossRf3Assertions
{
    private const int PlacementVersion = 1;

    internal static MessageInspection Acked(string id)
        => new(new(id, MessageState.Acked, 1, 3, 1, null, null, LeaseVersion: 1), null, null);

    internal static MessageInspection Leased(Delivery delivery, string principalId)
        => new(new(delivery.Id, MessageState.Leased, 1, 2, 1, null, null,
            LeaseOwner: principalId, LeaseVersion: 1, LeaseUntil: delivery.LeaseUntil),
            MultiLaneReceiveRf3Flow.Payload, MultiLaneReceiveRf3Flow.Headers);

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static async Task<RequestCqrsFaultMcpObservation> CallAsync(RequestCqrsRf3Callers caller,
        DeliveryCommand command, CancellationToken token)
    {
        try
        {
            return new(await caller.Mcp.CallAsync(McpCallerTools.MessagesComplete, command, token).ConfigureAwait(false), null);
        }
        catch (OperationCanceledException error)
        {
            return new(null, error);
        }
        catch (HttpRequestException error)
        {
            return new(null, error);
        }
        catch (IOException error)
        {
            return new(null, error);
        }
    }

    internal static async Task ReconcileAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        MultiLaneReceiveRequest request, DeliveryCommand ack, MessageInspection committed, MessageInspection ready,
        string principalId, CancellationToken token)
    {
        await RecoverReceiptAsync(administrator, caller, request, ack, committed, ready, token);
        await QueueAckResponseLossRf3Authority.VerifyAsync(administrator, caller, request, ack, committed, ready, principalId, token);
        await HealthyLaneAsync(administrator, caller, request, principalId, ready, token);
        await QueueAckResponseLossRf3Authority.RejectPairAsync(administrator, caller, request, ack, ErrorCode.PermissionDenied,
            committed, Acked(MultiLaneReceiveRf3Flow.Message), token);
    }

    private static async Task<CommitReceipt> RecoverReceiptAsync(RequestCqrsRf3Callers administrator,
        RequestCqrsRf3Callers caller, MultiLaneReceiveRequest request, DeliveryCommand ack,
        MessageInspection committed, MessageInspection ready, CancellationToken token)
    {
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteAsync(ack, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await ReceiptAsync(administrator, receipt, ack, MultiLaneReceiveRf3Flow.Message, token);
        await ImagesAsync(administrator, request, committed, ready, token);
        var official = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, ack, token).ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(official.Value, receipt);
        await ImagesAsync(administrator, request, committed, ready, token);
        var changed = ack with
        {
            Action = DeliveryAction.Nack
        };
        var denied = await caller.Sdk.CompleteAsync(changed, token).ConfigureAwait(false);
        await Assert.That(denied.IsSuccess).IsFalse();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(ErrorCode.Conflict.ToString());
        await ImagesAsync(administrator, request, committed, ready, token);
        await McpCallerAssertions.ErrorAsync(await caller.Mcp.CallAsync(McpCallerTools.MessagesComplete,
            changed, token).ConfigureAwait(false), ErrorCode.Conflict, dispatched: true).ConfigureAwait(false);
        await ImagesAsync(administrator, request, committed, ready, token);
        var again = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteAsync(ack, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(again, receipt);
        await ImagesAsync(administrator, request, committed, ready, token);
        return receipt;
    }

    private static async Task HealthyLaneAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        MultiLaneReceiveRequest request, string principalId, MessageInspection ready, CancellationToken token)
    {
        var next = request.Requests[1];
        var claim = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, next, token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(claim.Value.RequestId).IsEqualTo(next.RequestId);
        var delivery = await Assert.That(claim.Value.Deliveries).HasSingleItem();
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await EqualAsync(delivery, new Delivery(MultiLaneReceiveRf3Flow.Message, MultiLaneReceiveRf3Flow.Payload,
            MultiLaneReceiveRf3Flow.Headers, delivery.Token, 1, delivery.LeaseUntil, 1, 1));
        var leased = ready with
        {
            Metadata = ready.Metadata with
            {
                State = MessageState.Leased,
                StateVersion = 2,
                Attempts = 1,
                LeaseOwner = principalId,
                LeaseVersion = 1,
                LeaseUntil = delivery.LeaseUntil
            }
        };
        await ImagesAsync(administrator, request, Acked(delivery.Id), leased, token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReceiveAsync(next, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(replay, claim.Value);
        await ImagesAsync(administrator, request, Acked(delivery.Id), leased, token);
        await CompleteHealthyAsync(administrator, caller, request, delivery, token);
    }

    private static async Task CompleteHealthyAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        MultiLaneReceiveRequest request, Delivery delivery, CancellationToken token)
    {
        var next = request.Requests[1];
        var completion = new DeliveryCommand(Guid.NewGuid(), next.Lane, delivery.Token, DeliveryAction.Ack);
        var healthy = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, completion, token).ConfigureAwait(false)).ConfigureAwait(false);
        await ReceiptAsync(administrator, healthy.Value, completion, delivery.Id, token);
        await ImagesAsync(administrator, request, Acked(delivery.Id), Acked(delivery.Id), token);
        var replay = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteAsync(completion, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(replay, healthy.Value);
        await ImagesAsync(administrator, request, Acked(delivery.Id), Acked(delivery.Id), token);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReceiveAsync(
            new(Guid.NewGuid(), next.Lane), token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(empty.Deliveries).IsEmpty();
        await ImagesAsync(administrator, request, Acked(delivery.Id), Acked(delivery.Id), token);
    }

    private static async Task ReceiptAsync(RequestCqrsRf3Callers administrator, CommitReceipt receipt,
        DeliveryCommand command, string id, CancellationToken token)
    {
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadAtomicPartitionPlacementAsync(
            new(PlacementVersion, command.Lane.Partition), token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(placement.Partition).IsEqualTo(command.Lane.Partition);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        var expected = new CommitReceipt(command.CommandId,
            new(placement.Incarnation, command.Lane.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            [new("Ack", command.Lane.Queue, id, 3)], DurabilityProfile.QuorumProcessDurable);
        await EqualAsync(receipt, expected);
    }

    internal static async Task ImagesAsync(RequestCqrsRf3Callers administrator, MultiLaneReceiveRequest request,
        MessageInspection first, MessageInspection second, CancellationToken token)
    {
        await EqualAsync(await InspectAsync(administrator, request.Requests[0].Lane, token), first);
        await EqualAsync(await InspectAsync(administrator, request.Requests[1].Lane, token), second);
    }

    private static async Task<MessageInspection> InspectAsync(RequestCqrsRf3Callers administrator, QueueLaneRef lane, CancellationToken token)
        => await PublicReadAssertions.MessageAsync(await administrator.Sdk.InspectAsync(
            new(lane, MultiLaneReceiveRf3Flow.Message), token).ConfigureAwait(false)).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The ACK response-loss canonical message is absent.");
}
