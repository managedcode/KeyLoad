using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueFaultRf3Assertions
{
    private const string MissingDueRecord = "A persisted due record disappeared during autonomous RF3 execution.";

    internal static async Task AssertOutcomesAsync(NodeEpochRf3Callers first, NodeEpochRf3Callers second,
        DueFaultRf3Seed seed, CancellationToken cancellationToken)
    {
        await AssertScheduleAsync(first, seed, cancellationToken).ConfigureAwait(false);
        await AssertSagaAsync(first, seed, cancellationToken).ConfigureAwait(false);
        await AssertMessageAsync(first, seed.RecurringLane, seed.OccurrenceId, seed.DueAt, null,
            DueFaultRf3Protocol.RecurringPayload, DueFaultRf3Protocol.RecurringHeaders, cancellationToken)
            .ConfigureAwait(false);
        await AssertMessageAsync(first, seed.TimeoutLane, seed.TimeoutMessageId, null,
            seed.DueAt.Add(DueFaultRf3Protocol.TimeoutTtl),
            DueFaultRf3Protocol.TimeoutPayload, DueFaultRf3Protocol.TimeoutHeaders, cancellationToken)
            .ConfigureAwait(false);
        await AssertScheduleAsync(second, seed, cancellationToken).ConfigureAwait(false);
        await AssertSagaAsync(second, seed, cancellationToken).ConfigureAwait(false);
        await AssertMessageAsync(second, seed.RecurringLane, seed.OccurrenceId, seed.DueAt, null,
            DueFaultRf3Protocol.RecurringPayload, DueFaultRf3Protocol.RecurringHeaders, cancellationToken)
            .ConfigureAwait(false);
        await AssertMessageAsync(second, seed.TimeoutLane, seed.TimeoutMessageId, null,
            seed.DueAt.Add(DueFaultRf3Protocol.TimeoutTtl),
            DueFaultRf3Protocol.TimeoutPayload, DueFaultRf3Protocol.TimeoutHeaders, cancellationToken)
            .ConfigureAwait(false);
    }

    internal static async Task<bool> AreBothTransitionsCommittedAsync(KeyLoadClient first, KeyLoadClient second,
        DueFaultRf3Seed seed, CancellationToken cancellationToken)
    {
        var a = await McpCallerAssertions.SdkSuccessAsync(await first.InspectRecurringScheduleAsync(
            new(seed.RecurringLane, seed.ScheduleId), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var b = await McpCallerAssertions.SdkSuccessAsync(await second.InspectSagaAsync(
            new(seed.SagaLane, seed.SagaId), cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        if (a is null || b is null)
        { throw new InvalidOperationException(MissingDueRecord); }
        return a.NextOrdinal == 1 && b.Phase == SagaPhase.TimedOut && b.Revision == 2;
    }

    private static async Task AssertScheduleAsync(NodeEpochRf3Callers callers, DueFaultRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var request = new InspectRecurringScheduleRequest(seed.RecurringLane, seed.ScheduleId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectRecurringScheduleAsync(request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<RecurringScheduleInspection?>(await callers.Mcp.CallAsync(
            DueFaultRf3Protocol.InspectScheduleTool, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk).IsNotNull();
        await Assert.That(mcp.Value).IsNotNull();
        await Assert.That(sdk!.Revision).IsEqualTo(1L);
        await Assert.That(sdk.Generation).IsEqualTo(1L);
        await Assert.That(sdk.NextOrdinal).IsEqualTo(1L);
        await Assert.That(sdk.Cancelled).IsFalse();
        await Assert.That(sdk.Definition.FirstDueAt).IsEqualTo(seed.DueAt);
        await Assert.That(sdk.Definition.Interval).IsEqualTo(DueFaultRf3Protocol.RecurrenceInterval);
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value!))).IsTrue();
        await Assert.That(sdk.Definition).IsEqualTo(seed.Schedule);
    }

    private static async Task AssertSagaAsync(NodeEpochRf3Callers callers, DueFaultRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var request = new InspectSagaRequest(seed.SagaLane, seed.SagaId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectSagaAsync(request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<SagaInspection?>(await callers.Mcp.CallAsync(
            DueFaultRf3Protocol.InspectSagaTool, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk).IsNotNull();
        await Assert.That(mcp.Value).IsNotNull();
        await Assert.That(sdk!.Revision).IsEqualTo(2L);
        await Assert.That(sdk.Phase).IsEqualTo(SagaPhase.TimedOut);
        await Assert.That(sdk.Deadline).IsEqualTo(seed.DueAt);
        await Assert.That(sdk.StateJson).IsEqualTo("{}");
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value!))).IsTrue();
    }

    private static async Task AssertMessageAsync(NodeEpochRf3Callers callers, QueueLaneRef lane, string id,
        DateTimeOffset? notBefore, DateTimeOffset? expiresAt, string payload, string headers,
        CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(lane, id);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(request, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<MessageInspection?>(await callers.Mcp.CallAsync(
            DueFaultRf3Protocol.InspectMessageTool, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk).IsNotNull();
        await Assert.That(mcp.Value).IsNotNull();
        await Assert.That(sdk!.Metadata.State).IsEqualTo(MessageState.Ready);
        await Assert.That(sdk.Metadata.NotBefore).IsEqualTo(notBefore);
        await Assert.That(sdk.Metadata.ExpiresAt).IsEqualTo(expiresAt);
        await Assert.That(sdk.PayloadJson).IsEqualTo(payload);
        await Assert.That(sdk.HeadersJson).IsEqualTo(headers);
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value!))).IsTrue();
    }

    internal static async Task AcknowledgeAndProveExhaustionAsync(NodeEpochRf3Callers callers,
        DueFaultRf3Seed seed, CancellationToken cancellationToken)
    {
        await AcknowledgeAsync(callers, seed.RecurringLane, seed.OccurrenceId, seed.Schedule.PayloadJson,
            seed.Schedule.HeadersJson, cancellationToken).ConfigureAwait(false);
        await AcknowledgeAsync(callers, seed.TimeoutLane, seed.TimeoutMessageId,
            DueFaultRf3Protocol.TimeoutPayload, DueFaultRf3Protocol.TimeoutHeaders, cancellationToken)
            .ConfigureAwait(false);
        await AssertNoSecondTimeoutAsync(callers, seed, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AcknowledgeAsync(NodeEpochRf3Callers callers, QueueLaneRef lane, string id,
        string payload, string headers, CancellationToken cancellationToken)
    {
        var receive = new ReceiveRequest(Guid.NewGuid(), lane, DueFaultRf3Protocol.PageMessages);
        var leased = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(receive,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(leased.Deliveries).HasSingleItem();
        var delivery = leased.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(id);
        await Assert.That(delivery.PayloadJson).IsEqualTo(payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(headers);
        var command = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var mcp = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesComplete, command, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(command, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value))).IsTrue();
        var message = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(new(lane, id),
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Acked);
        await AssertEmptyQueueAsync(callers, lane, cancellationToken).ConfigureAwait(false);
    }

    private static async Task AssertEmptyQueueAsync(NodeEpochRf3Callers callers, QueueLaneRef lane,
        CancellationToken cancellationToken)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), lane, DueFaultRf3Protocol.PageMessages);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk.Deliveries).IsEmpty();
        await Assert.That(mcp.Value.Deliveries).IsEmpty();
    }

    private static async Task AssertNoSecondTimeoutAsync(NodeEpochRf3Callers callers, DueFaultRf3Seed seed,
        CancellationToken cancellationToken)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), seed.TimeoutLane, DueFaultRf3Protocol.PageMessages);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await callers.Mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await Assert.That(sdk.Deliveries).IsEmpty();
        await Assert.That(mcp.Value.Deliveries).IsEmpty();
        await AssertSagaAsync(callers, seed, cancellationToken).ConfigureAwait(false);
    }
}
