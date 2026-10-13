using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueParkedPolicyRf3Seed
{
    internal static async Task<QueueParkedPolicyRf3State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers admin, CancellationToken token)
    {
        var partition = new PartitionRef(QueueParkedPolicyRf3Protocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueParkedPolicyRf3Protocol.Database, QueueParkedPolicyRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(QueueParkedPolicyRf3Protocol.Queue, ResourceKind.WorkQueue,
            partition.TransactionDomainId)
        {
            QueuePolicy = new()
            {
                MaxStoredMessages = 2,
                OrderingProfile = QueueOrderingProfile.StrictPerKey,
                ParkedHeadPolicy = QueueParkedHeadPolicy.Block
            }
        };
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var id = Guid.NewGuid();
        var configured = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token));
        await QueueParkedPolicyRf3Assertions.EqualAsync(configured, definition);
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(QueueParkedPolicyRf3Protocol.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)), token));
        var identity = await MessagingRf3Identity.CreateAsync(fixture, partition.TenantId,
            [new(partition.DatabaseId, QueueParkedPolicyRf3Protocol.Queue, Capability.QueuePublish | Capability.QueueConsume |
                Capability.QueueAck | Capability.QueueInspect | Capability.QueueCancel),
             new(partition.DatabaseId, QueueParkedPolicyRf3Protocol.Collection, Capability.DocumentsRead)], [], false, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        await QueueParkedPolicyRf3Assertions.EqualAsync(placement,
            (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await admin.Mcp.CallAsync(
                McpCallerTools.AdminPartitionPlacementRead, new AtomicPartitionPlacementReadRequest(1, partition), token))).Value);
        await Assert.That(placement.Partition).IsEqualTo(partition);
        await Assert.That(placement.Incarnation).IsNotEqualTo(Guid.Empty);
        var state = new QueueParkedPolicyRf3State(partition, definition, identity, placement);
        state.Configurations.Add((id, request, configured));
        return state;
    }

    internal static async Task PublishAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, bool fresh, CancellationToken token)
    {
        var head = fresh ? QueueParkedPolicyRf3Protocol.FreshHead : QueueParkedPolicyRf3Protocol.OriginalHead;
        var sibling = fresh ? QueueParkedPolicyRf3Protocol.FreshSibling : QueueParkedPolicyRf3Protocol.OriginalSibling;
        var due = TimeProvider.System.GetUtcNow().AddHours(QueueParkedPolicyRf3Protocol.FutureHours);
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new EnqueueMessage(state.Lane.Queue, head, QueueParkedPolicyRf3Protocol.Payload, QueueParkedPolicyRf3Protocol.Headers,
                NotBefore: due, OrderingKey: QueueParkedPolicyRf3Protocol.OrderingKey),
             new EnqueueMessage(state.Lane.Queue, sibling, QueueParkedPolicyRf3Protocol.Payload, QueueParkedPolicyRf3Protocol.Headers,
                OrderingKey: QueueParkedPolicyRf3Protocol.OrderingKey)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CommitAsync(command, token));
        await QueueParkedPolicyRf3Assertions.ReceiptAsync(receipt, command.CommandId, state,
            [new(QueueParkedPolicyRf3Protocol.Enqueue, state.Lane.Queue, head, 1L),
             new(QueueParkedPolicyRf3Protocol.Enqueue, state.Lane.Queue, sibling, 1L)]);
        state.Commands.Add((command, receipt));
        state.Messages[head] = new(new(head, MessageState.Scheduled, 0, 1L, 0L, due, null,
            EnqueueSequence: fresh ? 0L : 1L, ActiveOrderSequence: fresh ? 0L : 1L),
            QueueParkedPolicyRf3Protocol.Payload, QueueParkedPolicyRf3Protocol.Headers);
        state.Messages[sibling] = new(new(sibling, MessageState.Ready, 0, 1L, fresh ? 2L : 1L, null, null,
            EnqueueSequence: fresh ? 0L : 2L, ActiveOrderSequence: fresh ? 0L : 2L),
            QueueParkedPolicyRf3Protocol.Payload, QueueParkedPolicyRf3Protocol.Headers);
        await QueueParkedPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyRf3Replay.CommandAsync(worker, command, receipt,
            () => QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
    }
}
