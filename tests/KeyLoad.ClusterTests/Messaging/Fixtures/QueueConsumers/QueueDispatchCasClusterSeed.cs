using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueDispatchCasClusterSeed
{
    internal static Task<QueueDispatchCasClusterState> RunAsync(ClusterFixture fixture, string subject, CancellationToken token)
        => QueueDispatchCasClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async (sdk, mcp) =>
        {
            var tenant = QueueDispatchCasClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
            var partition = new PartitionRef(tenant, QueueDispatchCasClusterProtocol.Database,
                QueueDispatchCasClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            var lane = new QueueLaneRef(partition, QueueDispatchCasClusterProtocol.Queue);
            var definition = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
            {
                FieldPolicies = [new(QueueDispatchCasClusterProtocol.PrivateField, QueueDispatchCasClusterProtocol.Classification)],
                HeaderPolicies = [new(QueueDispatchCasClusterProtocol.PrivateHeader, QueueDispatchCasClusterProtocol.Classification)],
                QueuePolicy = new() { MaxAttempts = QueueDispatchCasClusterProtocol.Attempts }
            };
            await QueueDispatchCasClusterCommands.ResultAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.ConfigureResourceAsync(Guid.NewGuid(), new(tenant, partition.DatabaseId, definition), token)), definition);
            var member = await MessagingClusterIdentity.CreateSelectedAsync(fixture, subject, tenant,
                [new(partition.DatabaseId, lane.Queue, Capability.QueueInspect)], [], false, token);
            var original = new CommandRequest(Guid.NewGuid(), partition,
                [new EnqueueMessage(lane.Queue, QueueDispatchCasClusterProtocol.Message,
                    QueueDispatchCasClusterProtocol.Payload, QueueDispatchCasClusterProtocol.Headers)]);
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, token));
            await QueueDispatchCasClusterReceipts.SeedAsync(receipt, original.CommandId, lane);
            var outbox = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(partition, token));
            var message = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
                new(lane, QueueDispatchCasClusterProtocol.Message), token)) ?? throw new InvalidOperationException();
            await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Ready);
            await Assert.That(message.PayloadJson).IsEqualTo(QueueDispatchCasClusterProtocol.Payload);
            await Assert.That(message.HeadersJson).IsEqualTo(QueueDispatchCasClusterProtocol.Headers);
            var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.BrowseQueueAsync(new(lane), token));
            await Assert.That(page.Items).HasSingleItem();
            return new QueueDispatchCasClusterState(lane, definition, member, original, receipt, message,
                JsonDefaults.Serialize(page.Counters), JsonDefaults.Serialize(page.Items), outbox);
        }, token);
}
