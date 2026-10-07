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
        CancellationToken token)
    {
        var first = request.Requests[0];
        var recovered = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReceiveAsync(first, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var native = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, first, token).ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(native.Value, recovered).ConfigureAwait(false);
        await Assert.That(recovered.RequestId).IsEqualTo(first.RequestId);
        var delivery = await Assert.That(recovered.Deliveries).HasSingleItem();
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(committedFirst.Metadata.LeaseUntil!.Value);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(committedFirst.Metadata.LeaseVersion);
        await Assert.That(delivery.Attempt).IsEqualTo(committedFirst.Metadata.Attempts);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(committedFirst.Metadata.DeliveryGeneration);
        await EqualAsync(await InspectAsync(administrator, first.Lane, token), committedFirst).ConfigureAwait(false);
        await EqualAsync(await InspectAsync(administrator, request.Requests[1].Lane, token), beforeSecond).ConfigureAwait(false);
        var result = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReceiveAcrossLanesAsync(request, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Outcomes.Length).IsEqualTo(2);
        await Assert.That(result.StopError).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        await EqualAsync(result.Outcomes[0].Result, recovered).ConfigureAwait(false);
        for (var index = 0; index < result.Outcomes.Length; index++)
        {
            await MultiLaneReceiveRf3Flow.CommittedAsync(administrator.Sdk, result.Outcomes[index],
                request.Requests[index], token).ConfigureAwait(false);
        }
        var after = new[] { await InspectAsync(administrator, first.Lane, token),
            await InspectAsync(administrator, request.Requests[1].Lane, token) };
        var replay = await McpCallerAssertions.SuccessAsync<MultiLaneReceiveResult>(await caller.Mcp.CallAsync(
            MultiLaneReceiveProtocol.ToolName, request, token).ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(replay.Value, result).ConfigureAwait(false);
        for (var index = 0; index < result.Outcomes.Length; index++)
        {
            await EqualAsync(await InspectAsync(administrator, request.Requests[index].Lane, token), after[index]);
            await AckAsync(administrator, caller, request.Requests[index].Lane,
                result.Outcomes[index].Result!.Deliveries[0], 1, token).ConfigureAwait(false);
        }
        await HealthyAsync(administrator, caller, first.Lane, token).ConfigureAwait(false);
    }

    private static async Task AckAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        QueueLaneRef lane, Delivery delivery, long readySequence, CancellationToken token)
    {
        var command = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var result = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CompleteAsync(command, token)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var replay = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await caller.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, command, token).ConfigureAwait(false)).ConfigureAwait(false);
        await EqualAsync(replay.Value, result).ConfigureAwait(false);
        await Assert.That(result.CommandId).IsEqualTo(command.CommandId);
        var inspected = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.InspectAsync(
            new(lane, delivery.Id), token).ConfigureAwait(false)).ConfigureAwait(false);
        var expected = new MessageInspection(new(delivery.Id, MessageState.Acked, 1, 3, readySequence,
            null, null, LeaseVersion: 1), null, null);
        await EqualAsync(inspected, expected).ConfigureAwait(false);
        await Assert.That(result.Mutations.Length).IsEqualTo(1);
        await Assert.That(result.Mutations[0]).IsEqualTo(new MutationReceipt("Ack", lane.Queue, delivery.Id, 3));
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(lane.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(result.Token.Position).IsGreaterThan(0L);
    }

    private static async Task HealthyAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers caller,
        QueueLaneRef lane, CancellationToken token)
    {
        const string id = "healthy";
        const string payload = "{\"work\":2}";
        const string headers = "{\"kind\":\"next\"}";
        await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.CommitAsync(new(Guid.NewGuid(), lane.Partition,
            [new EnqueueMessage(lane.Queue, id, payload, headers)]), token).ConfigureAwait(false)).ConfigureAwait(false);
        var request = new MultiLaneReceiveRequest(Guid.NewGuid(), [new(Guid.NewGuid(), lane)]);
        var result = await McpCallerAssertions.SuccessAsync<MultiLaneReceiveResult>(await caller.Mcp.CallAsync(
            MultiLaneReceiveProtocol.ToolName, request, token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(result.Value.RequestId).IsEqualTo(request.RequestId);
        await Assert.That(result.Value.Outcomes.Length).IsEqualTo(1);
        await Assert.That(result.Value.StopError).IsNull();
        await Assert.That(result.Value.SafeDetail).IsNull();
        var outcome = result.Value.Outcomes[0];
        await Assert.That(outcome.Status).IsEqualTo(QueueLaneReceiveStatus.Committed);
        await Assert.That(outcome.RequestId).IsEqualTo(request.Requests[0].RequestId);
        await Assert.That(outcome.Lane).IsEqualTo(lane);
        await Assert.That(outcome.Error).IsNull();
        await Assert.That(outcome.SafeDetail).IsNull();
        await Assert.That(outcome.Result!.RequestId).IsEqualTo(request.Requests[0].RequestId);
        var delivery = await Assert.That(outcome.Result.Deliveries).HasSingleItem();
        await Assert.That(delivery.Token).IsNotEqualTo(string.Empty);
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(headers);
        await Assert.That(delivery.Attempt).IsEqualTo(1);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(1L);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(1L);
        var inspected = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.InspectAsync(
            new(lane, id), token).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(inspected!.Metadata.State).IsEqualTo(MessageState.Leased);
        await Assert.That(inspected.Metadata.LeaseUntil).IsEqualTo(delivery.LeaseUntil);
        await Assert.That(inspected.PayloadJson).IsEqualTo(payload);
        await Assert.That(inspected.HeadersJson).IsEqualTo(headers);
        await AckAsync(administrator, caller, lane, delivery, 2, token).ConfigureAwait(false);
    }

    private static async Task<MessageInspection?> InspectAsync(RequestCqrsRf3Callers administrator,
        QueueLaneRef lane, CancellationToken token)
        => await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.InspectAsync(
            new(lane, MultiLaneReceiveRf3Flow.Message), token).ConfigureAwait(false)).ConfigureAwait(false);
}
