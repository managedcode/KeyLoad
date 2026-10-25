using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.ResourceExecution;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueTerminalPolicyClusterSeed
{
    internal static async Task<QueueTerminalPolicyClusterState> CreateAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers admin, CancellationToken token)
    {
        var partition = new PartitionRef(QueueTerminalPolicyClusterProtocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueTerminalPolicyClusterProtocol.Database, QueueTerminalPolicyClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(QueueTerminalPolicyClusterProtocol.Queue, ResourceKind.WorkQueue,
            partition.TransactionDomainId)
        {
            QueuePolicy = new()
            {
                MaxAttempts = QueueTerminalPolicyClusterProtocol.Attempts,
                MaxStoredMessages = 2,
                MaxDeadLetterMessages = 1
            }
        };
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var id = Guid.NewGuid();
        var configured = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token));
        await QueueTerminalPolicyClusterAssertions.EqualAsync(configured, definition);
        var collectionId = Guid.NewGuid();
        var collection = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, new(QueueTerminalPolicyClusterProtocol.Collection,
            ResourceKind.Collection, partition.TransactionDomainId));
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(collectionId, collection, token));
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, partition.TenantId,
            [new(partition.DatabaseId, QueueTerminalPolicyClusterProtocol.Queue, Capability.QueuePublish | Capability.QueueConsume |
                Capability.QueueAck | Capability.QueueInspect | Capability.DeadLettersRead),
             new(partition.DatabaseId, QueueTerminalPolicyClusterProtocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)],
            [], false, token);
        var state = new QueueTerminalPolicyClusterState(partition, definition, identity)
        {
            CollectionLifetime = ResourceCreationLifetime.Create(collectionId, collection)
        };
        state.Configurations.Add((id, request, configured));
        return state;
    }

    internal static async Task PublishAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, bool healthy, CancellationToken token)
    {
        var id = healthy ? QueueTerminalPolicyClusterProtocol.Healthy : QueueTerminalPolicyClusterProtocol.Message;
        Mutation[] mutations = healthy
            ? [new PutDocument(QueueTerminalPolicyClusterProtocol.Collection, id, QueueTerminalPolicyClusterProtocol.Payload),
                new EnqueueMessage(state.Lane.Queue, id, QueueTerminalPolicyClusterProtocol.Payload, QueueTerminalPolicyClusterProtocol.Headers)]
            : [new EnqueueMessage(state.Lane.Queue, id, QueueTerminalPolicyClusterProtocol.Payload, QueueTerminalPolicyClusterProtocol.Headers)];
        var command = new CommandRequest(Guid.NewGuid(), state.Partition, [.. mutations]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CommitAsync(command, token));
        MutationReceipt[] expected = healthy
            ? [new(QueueTerminalPolicyClusterProtocol.Put, QueueTerminalPolicyClusterProtocol.Collection, id, 1L),
                new(QueueTerminalPolicyClusterProtocol.Enqueue, state.Lane.Queue, id, 1L)]
            : [new(QueueTerminalPolicyClusterProtocol.Enqueue, state.Lane.Queue, id, 1L)];
        await QueueTerminalPolicyClusterAssertions.ReceiptAsync(receipt, command.CommandId, state, expected);
        state.Producers.Add((command, receipt));
        if (healthy)
        {
            state.Healthy = new(new(id, MessageState.Ready, 0, 1L, 4L, null, null),
            QueueTerminalPolicyClusterProtocol.Payload, QueueTerminalPolicyClusterProtocol.Headers);
        }
        await QueueTerminalPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await QueueTerminalPolicyClusterReplay.ProducerAsync(worker, command, receipt,
            () => QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
    }
}
