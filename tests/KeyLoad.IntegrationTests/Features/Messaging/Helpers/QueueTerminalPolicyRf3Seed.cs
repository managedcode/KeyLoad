using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyRf3Seed
{
    internal static async Task<QueueTerminalPolicyRf3State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers admin, CancellationToken token)
    {
        var partition = new PartitionRef(QueueTerminalPolicyRf3Protocol.Tenant + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat),
            QueueTerminalPolicyRf3Protocol.Database, QueueTerminalPolicyRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
        var definition = new ResourceDefinition(QueueTerminalPolicyRf3Protocol.Queue, ResourceKind.WorkQueue,
            partition.TransactionDomainId)
        {
            QueuePolicy = new()
            {
                MaxAttempts = QueueTerminalPolicyRf3Protocol.Attempts,
                MaxStoredMessages = 2,
                MaxDeadLetterMessages = 1
            }
        };
        var request = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var id = Guid.NewGuid();
        var configured = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token));
        await QueueTerminalPolicyRf3Assertions.EqualAsync(configured, definition);
        await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new(QueueTerminalPolicyRf3Protocol.Collection,
                ResourceKind.Collection, partition.TransactionDomainId)), token));
        var identity = await MessagingRf3Identity.CreateAsync(fixture, partition.TenantId,
            [new(partition.DatabaseId, QueueTerminalPolicyRf3Protocol.Queue, Capability.QueuePublish | Capability.QueueConsume |
                Capability.QueueAck | Capability.QueueInspect | Capability.DeadLettersRead),
             new(partition.DatabaseId, QueueTerminalPolicyRf3Protocol.Collection, Capability.DocumentsRead | Capability.DocumentsWrite)],
            [], false, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        await QueueTerminalPolicyRf3Assertions.EqualAsync(placement,
            (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await admin.Mcp.CallAsync(
                McpCallerTools.AdminPartitionPlacementRead, new AtomicPartitionPlacementReadRequest(1, partition), token))).Value);
        await Assert.That(placement.Partition).IsEqualTo(partition);
        await Assert.That(placement.Incarnation).IsNotEqualTo(Guid.Empty);
        var state = new QueueTerminalPolicyRf3State(partition, definition, identity, placement);
        state.Configurations.Add((id, request, configured));
        return state;
    }

    internal static async Task PublishAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, bool healthy, CancellationToken token)
    {
        var id = healthy ? QueueTerminalPolicyRf3Protocol.Healthy : QueueTerminalPolicyRf3Protocol.Message;
        Mutation[] mutations = healthy
            ? [new PutDocument(QueueTerminalPolicyRf3Protocol.Collection, id, QueueTerminalPolicyRf3Protocol.Payload),
                new EnqueueMessage(state.Lane.Queue, id, QueueTerminalPolicyRf3Protocol.Payload, QueueTerminalPolicyRf3Protocol.Headers)]
            : [new EnqueueMessage(state.Lane.Queue, id, QueueTerminalPolicyRf3Protocol.Payload, QueueTerminalPolicyRf3Protocol.Headers)];
        var command = new CommandRequest(Guid.NewGuid(), state.Partition, [.. mutations]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await worker.Sdk.CommitAsync(command, token));
        MutationReceipt[] expected = healthy
            ? [new(QueueTerminalPolicyRf3Protocol.Put, QueueTerminalPolicyRf3Protocol.Collection, id, 1L),
                new(QueueTerminalPolicyRf3Protocol.Enqueue, state.Lane.Queue, id, 1L)]
            : [new(QueueTerminalPolicyRf3Protocol.Enqueue, state.Lane.Queue, id, 1L)];
        await QueueTerminalPolicyRf3Assertions.ReceiptAsync(receipt, command.CommandId, state, expected);
        state.Producers.Add((command, receipt));
        if (healthy)
        {
            state.Healthy = new(new(id, MessageState.Ready, 0, 1L, 4L, null, null),
            QueueTerminalPolicyRf3Protocol.Payload, QueueTerminalPolicyRf3Protocol.Headers);
        }
        await QueueTerminalPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await QueueTerminalPolicyRf3Replay.ProducerAsync(worker, command, receipt,
            () => QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
    }
}
