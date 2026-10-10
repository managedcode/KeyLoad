using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadLetterRf3Scenario
{
    internal static async Task<QueueDeadLetterRf3State> SeedAsync(RequestCqrsRf3Callers callers,
        bool mcpFirst, CancellationToken token)
    {
        var partition = new PartitionRef(QueueDeadLetterRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueDeadLetterRf3Protocol.Database, QueueDeadLetterRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        foreach (var queue in new[] { QueueDeadLetterRf3Protocol.Queue, QueueDeadLetterRf3Protocol.HealthyQueue })
        {
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
                { QueuePolicy = new() { MaxAttempts = QueueDeadLetterRf3Protocol.One, MaxStoredMessages = QueueDeadLetterRf3Protocol.One } }), token));
        }
        await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(QueueDeadLetterRf3Protocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId)), token));
        var producer = new CommandRequest(Guid.NewGuid(), partition,
            [new EnqueueMessage(QueueDeadLetterRf3Protocol.Queue, QueueDeadLetterRf3Protocol.Message,
                QueueDeadLetterRf3Protocol.Payload, QueueDeadLetterRf3Protocol.Headers, OrderingKey: QueueDeadLetterRf3Protocol.OrderingKey)]);
        var produced = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(producer, token));
        var request = new ReceiveRequest(Guid.NewGuid(), new(partition, QueueDeadLetterRf3Protocol.Queue),
            LeaseSeconds: QueueDeadLetterRf3Protocol.LeaseSeconds);
        var received = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await QueueDeadLetterRf3Assertions.DeliveryAsync(delivery, QueueDeadLetterRf3Protocol.Message);
        await QueueDeadLetterRf3Assertions.LeasedAsync(callers, request.Lane, delivery, QueueDeadLetterRf3Protocol.First, token);
        var nack = new DeliveryCommand(Guid.NewGuid(), request.Lane, delivery.Token, DeliveryAction.Nack);
        var completed = mcpFirst
            ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, nack, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(nack, token));
        var refused = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(QueueDeadLetterRf3Protocol.Collection, QueueDeadLetterRf3Protocol.Refused, QueueDeadLetterRf3Protocol.Payload),
             new EnqueueMessage(request.Lane.Queue, QueueDeadLetterRf3Protocol.Refused, QueueDeadLetterRf3Protocol.Payload)]);
        await QueueDeadLetterRf3Assertions.MutationAsync(produced, producer.CommandId, request.Lane,
            QueueDeadLetterRf3Protocol.Message, QueueDeadLetterRf3Protocol.EnqueueKind, QueueDeadLetterRf3Protocol.First);
        await QueueDeadLetterRf3Assertions.MutationAsync(completed, nack.CommandId, request.Lane,
            QueueDeadLetterRf3Protocol.Message, DeliveryAction.Nack.ToString(), QueueDeadLetterRf3Protocol.TerminalVersion);
        return new(partition, producer, produced, nack, completed, refused);
    }

    internal static async Task HealthyAsync(RequestCqrsRf3Callers callers, QueueDeadLetterRf3State state, long sequence, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new EnqueueMessage(state.HealthyLane.Queue, id, QueueDeadLetterRf3Protocol.Payload, QueueDeadLetterRf3Protocol.Headers)]);
        var produced = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await QueueDeadLetterRf3Assertions.ReceiptAsync(callers, command, produced, token);
        var request = new ReceiveRequest(Guid.NewGuid(), state.HealthyLane, LeaseSeconds: QueueDeadLetterRf3Protocol.LeaseSeconds);
        var received = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value;
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await QueueDeadLetterRf3Assertions.DeliveryAsync(delivery, id);
        await QueueDeadLetterRf3Assertions.LeasedAsync(callers, state.HealthyLane, delivery, sequence, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.HealthyLane, delivery.Token, DeliveryAction.Ack);
        var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(ack, token));
        var replay = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token))).Value;
        await QueueDeadLetterRf3Assertions.EqualAsync(original, replay);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(new(state.HealthyLane, id), token));
        await Assert.That(actual!.Metadata).IsEqualTo(new MessageMetadata(id, MessageState.Acked, QueueDeadLetterRf3Protocol.One,
            QueueDeadLetterRf3Protocol.TerminalVersion, sequence, null, null, LeaseVersion: QueueDeadLetterRf3Protocol.First));
        await Assert.That(actual.PayloadJson).IsNull();
        await Assert.That(actual.HeadersJson).IsNull();
    }
}
