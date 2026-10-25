using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueParkedPolicyClusterSeed
{
    internal static async Task<QueueParkedPolicyClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var partition = new PartitionRef(QueueParkedPolicyClusterProtocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueParkedPolicyClusterProtocol.Database, QueueParkedPolicyClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(QueueParkedPolicyClusterProtocol.Queue, ResourceKind.WorkQueue,
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
        await QueueParkedPolicyClusterAssertions.EqualAsync(configured, definition);
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(QueueParkedPolicyClusterProtocol.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)), token));
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, partition.TenantId,
            [new(partition.DatabaseId, QueueParkedPolicyClusterProtocol.Queue, Capability.QueuePublish | Capability.QueueConsume |
                Capability.QueueAck | Capability.QueueInspect | Capability.QueueCancel),
             new(partition.DatabaseId, QueueParkedPolicyClusterProtocol.Collection, Capability.DocumentsRead)], [], true, token);
        var state = new QueueParkedPolicyClusterState(partition, definition, identity);
        state.Configurations.Add((id, request, configured));
        return state;
    }

    internal static async Task PublishAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, bool fresh, CancellationToken token)
    {
        var head = fresh ? QueueParkedPolicyClusterProtocol.FreshHead : QueueParkedPolicyClusterProtocol.OriginalHead;
        var sibling = fresh ? QueueParkedPolicyClusterProtocol.FreshSibling : QueueParkedPolicyClusterProtocol.OriginalSibling;
        var due = TimeProvider.System.GetUtcNow().AddHours(QueueParkedPolicyClusterProtocol.FutureHours);
        var command = new CommandRequest(Guid.NewGuid(), state.Partition,
            [new EnqueueMessage(state.Lane.Queue, head, QueueParkedPolicyClusterProtocol.Payload, QueueParkedPolicyClusterProtocol.Headers,
                NotBefore: due, OrderingKey: QueueParkedPolicyClusterProtocol.OrderingKey),
             new EnqueueMessage(state.Lane.Queue, sibling, QueueParkedPolicyClusterProtocol.Payload, QueueParkedPolicyClusterProtocol.Headers,
                OrderingKey: QueueParkedPolicyClusterProtocol.OrderingKey)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CommitAsync(command, token));
        await QueueParkedPolicyClusterAssertions.ReceiptAsync(receipt, command.CommandId, state,
            [new(QueueParkedPolicyClusterProtocol.Enqueue, state.Lane.Queue, head, 1L),
             new(QueueParkedPolicyClusterProtocol.Enqueue, state.Lane.Queue, sibling, 1L)]);
        state.Commands.Add((command, receipt));
        state.Messages[head] = new(new(head, MessageState.Scheduled, 0, 1L, 0L, due, null,
            EnqueueSequence: fresh ? 0L : 1L, ActiveOrderSequence: fresh ? 0L : 1L),
            QueueParkedPolicyClusterProtocol.Payload, QueueParkedPolicyClusterProtocol.Headers);
        state.Messages[sibling] = new(new(sibling, MessageState.Ready, 0, 1L, fresh ? 2L : 1L, null, null,
            EnqueueSequence: fresh ? 0L : 2L, ActiveOrderSequence: fresh ? 0L : 2L),
            QueueParkedPolicyClusterProtocol.Payload, QueueParkedPolicyClusterProtocol.Headers);
        await QueueParkedPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await QueueParkedPolicyClusterReplay.CommandAsync(worker, command, receipt,
            () => QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
    }
}
