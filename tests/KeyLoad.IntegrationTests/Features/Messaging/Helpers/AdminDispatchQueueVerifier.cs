using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>Observes actual dispatch effects on one uniquely scoped persisted queue.</summary>
internal static class AdminDispatchQueueVerifier
{
    private const string Queue = "dispatch-jobs";
    private const string Message = "dispatch-message";
    private const string Payload = "{\"value\":\"dispatch-ready\"}";
    private const string Headers = "{}";
    private const int PageMessages = 1;

    internal static async Task<QueueLaneRef> SeedAsync(KeyLoadClient sdk, PartitionRef partition,
        CancellationToken cancellationToken)
    {
        var resource = new ResourceDefinition(Queue, ResourceKind.WorkQueue, partition.TransactionDomainId);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, resource), cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(new(Guid.NewGuid(), partition,
            [new EnqueueMessage(Queue, Message, Payload, Headers)]), cancellationToken));
        return new(partition, Queue);
    }

    internal static async Task AssertPausedAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueLaneRef lane,
        CancellationToken cancellationToken)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.BrowseQueueAsync(new(lane), cancellationToken));
        var request = new ReceiveRequest(Guid.NewGuid(), lane, PageMessages);
        var denied = await sdk.ReceiveAsync(request, cancellationToken);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.DispatchPaused));
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesReceive, request,
            cancellationToken), ErrorCode.DispatchPaused, dispatched: true);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.BrowseQueueAsync(new(lane), cancellationToken));
        await Assert.That(after.Counters).IsEqualTo(before.Counters);
        await Assert.That(after.Items.SequenceEqual(before.Items)).IsTrue();
    }

    internal static async Task DeliverAndAcknowledgeAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueLaneRef lane,
        CancellationToken cancellationToken)
    {
        var request = new ReceiveRequest(Guid.NewGuid(), lane, PageMessages);
        var received = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(request, cancellationToken));
        var replay = await McpCallerAssertions.SuccessAsync<ReceiveResult>(await mcp.CallAsync(
            McpCallerTools.MessagesReceive, request, cancellationToken));
        await Assert.That(JsonDefaults.Serialize(received).AsSpan().SequenceEqual(JsonDefaults.Serialize(replay.Value))).IsTrue();
        await Assert.That(received.Deliveries).HasSingleItem();
        var delivery = received.Deliveries[0];
        await Assert.That(delivery.Id).IsEqualTo(Message);
        await Assert.That(delivery.PayloadJson).IsEqualTo(Payload);
        await Assert.That(delivery.HeadersJson).IsEqualTo(Headers);
        var command = new DeliveryCommand(Guid.NewGuid(), lane, delivery.Token, DeliveryAction.Ack);
        var acknowledged = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.MessagesComplete, command, cancellationToken));
        var ackReplay = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteAsync(command, cancellationToken));
        await Assert.That(JsonDefaults.Serialize(ackReplay).AsSpan().SequenceEqual(JsonDefaults.Serialize(acknowledged.Value))).IsTrue();
        var message = await McpCallerAssertions.SdkSuccessAsync(await sdk.InspectAsync(new(lane, Message), cancellationToken));
        await Assert.That(message!.Metadata.State).IsEqualTo(MessageState.Acked);
        var empty = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReceiveAsync(
            new(Guid.NewGuid(), lane, PageMessages), cancellationToken));
        await Assert.That(empty.Deliveries).IsEmpty();
    }
}
