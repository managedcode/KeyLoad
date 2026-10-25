using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDeadLetterClusterScenario
{
    internal static async Task<QueueDeadLetterClusterState> SeedAsync(RequestCqrsClusterCallers callers,
        bool mcpFirst, CancellationToken token)
    {
        var partition = new PartitionRef(QueueDeadLetterClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueDeadLetterClusterProtocol.Database, QueueDeadLetterClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        foreach (var queue in new[] { QueueDeadLetterClusterProtocol.Queue, QueueDeadLetterClusterProtocol.HealthyQueue })
        {
            await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
                {
                    QueuePolicy = new()
                    {
                        MaxAttempts = QueueDeadLetterClusterProtocol.One,
                        MaxStoredMessages = QueueDeadLetterClusterProtocol.One
                    }
                }), token));
        }
        await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(QueueDeadLetterClusterProtocol.Collection, ResourceKind.Collection,
                partition.TransactionDomainId)), token));
        var producer = new CommandRequest(Guid.NewGuid(), partition,
            [new EnqueueMessage(QueueDeadLetterClusterProtocol.Queue, QueueDeadLetterClusterProtocol.Message,
                QueueDeadLetterClusterProtocol.Payload, QueueDeadLetterClusterProtocol.Headers, OrderingKey: QueueDeadLetterClusterProtocol.OrderingKey)]);
        var produced = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(producer, token));
        var request = new ReceiveRequest(Guid.NewGuid(), new(partition, QueueDeadLetterClusterProtocol.Queue),
            LeaseSeconds: QueueDeadLetterClusterProtocol.LeaseSeconds);
        var received = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveAsync(request, token));
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await QueueDeadLetterClusterAssertions.DeliveryAsync(delivery, QueueDeadLetterClusterProtocol.Message);
        await QueueDeadLetterClusterAssertions.LeasedAsync(callers, request.Lane, delivery, QueueDeadLetterClusterProtocol.First, token);
        var nack = new DeliveryCommand(Guid.NewGuid(), request.Lane, delivery.Token, DeliveryAction.Nack);
        var completed = mcpFirst
            ? (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, nack, token))).Value
            : await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(nack, token));
        var refused = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(QueueDeadLetterClusterProtocol.Collection, QueueDeadLetterClusterProtocol.Refused, QueueDeadLetterClusterProtocol.Payload),
             new EnqueueMessage(request.Lane.Queue, QueueDeadLetterClusterProtocol.Refused, QueueDeadLetterClusterProtocol.Payload)]);
        await QueueDeadLetterClusterAssertions.MutationAsync(produced, producer.CommandId, request.Lane,
            QueueDeadLetterClusterProtocol.Message, QueueDeadLetterClusterProtocol.EnqueueKind, QueueDeadLetterClusterProtocol.First);
        await QueueDeadLetterClusterAssertions.MutationAsync(completed, nack.CommandId, request.Lane,
            QueueDeadLetterClusterProtocol.Message, DeliveryAction.Nack.ToString(), QueueDeadLetterClusterProtocol.TerminalVersion);
        return new(partition, producer, produced, nack, completed, refused);
    }

    internal static async Task HealthyAsync(RequestCqrsClusterCallers callers, QueueDeadLetterClusterState state, long sequence, CancellationToken token)
    {
        var id = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new EnqueueMessage(state.HealthyLane.Queue, id, QueueDeadLetterClusterProtocol.Payload, QueueDeadLetterClusterProtocol.Headers)]);
        var produced = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await QueueDeadLetterClusterAssertions.ReceiptAsync(callers, command, produced, token);
        var request = new ReceiveRequest(Guid.NewGuid(), state.HealthyLane, LeaseSeconds: QueueDeadLetterClusterProtocol.LeaseSeconds);
        var received = (await McpCallerAssertions.SuccessAsync<ReceiveResult>(await callers.Mcp.CallAsync(McpCallerTools.MessagesReceive, request, token))).Value;
        await Assert.That(received.RequestId).IsEqualTo(request.RequestId);
        var delivery = await Assert.That(received.Deliveries).HasSingleItem();
        await QueueDeadLetterClusterAssertions.DeliveryAsync(delivery, id);
        await QueueDeadLetterClusterAssertions.LeasedAsync(callers, state.HealthyLane, delivery, sequence, token);
        var ack = new DeliveryCommand(Guid.NewGuid(), state.HealthyLane, delivery.Token, DeliveryAction.Ack);
        var original = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CompleteAsync(ack, token));
        var replay = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await callers.Mcp.CallAsync(McpCallerTools.MessagesComplete, ack, token))).Value;
        await QueueDeadLetterClusterAssertions.EqualAsync(original, replay);
        var actual = await PublicReadAssertions.MessageAsync(await callers.Sdk.InspectAsync(new(state.HealthyLane, id), token));
        await Assert.That(actual!.Metadata).IsEqualTo(new MessageMetadata(id, MessageState.Acked, QueueDeadLetterClusterProtocol.One,
            QueueDeadLetterClusterProtocol.TerminalVersion, sequence, null, null, LeaseVersion: QueueDeadLetterClusterProtocol.First));
        await Assert.That(actual.PayloadJson).IsNull();
        await Assert.That(actual.HeadersJson).IsNull();
    }
}
