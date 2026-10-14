using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasRf3Seed
{
    internal static Task<QueueDispatchCasRf3State> RunAsync(ClusterFixture fixture, string subject, CancellationToken token)
        => QueueDispatchCasRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async (sdk, mcp) =>
        {
            var tenant = QueueDispatchCasRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
            var partition = new PartitionRef(tenant, QueueDispatchCasRf3Protocol.Database,
                QueueDispatchCasRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            var lane = new QueueLaneRef(partition, QueueDispatchCasRf3Protocol.Queue);
            var definition = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId)
            {
                FieldPolicies = [new(QueueDispatchCasRf3Protocol.PrivateField, QueueDispatchCasRf3Protocol.Classification)],
                HeaderPolicies = [new(QueueDispatchCasRf3Protocol.PrivateHeader, QueueDispatchCasRf3Protocol.Classification)],
                QueuePolicy = new() { MaxAttempts = QueueDispatchCasRf3Protocol.Attempts }
            };
            await QueueDispatchCasRf3Commands.ResultAsync(await McpCallerAssertions.SdkSuccessAsync(
                await sdk.ConfigureResourceAsync(Guid.NewGuid(), new(tenant, partition.DatabaseId, definition), token)), definition);
            var member = await MessagingRf3Identity.CreateSelectedAsync(fixture, subject, tenant,
                [new(partition.DatabaseId, lane.Queue, Capability.QueueInspect)], [], false, token);
            var original = new CommandRequest(Guid.NewGuid(), partition,
                [new EnqueueMessage(lane.Queue, QueueDispatchCasRf3Protocol.Message,
                    QueueDispatchCasRf3Protocol.Payload, QueueDispatchCasRf3Protocol.Headers)]);
            var placement = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(new(QueueDispatchCasRf3Protocol.PlacementVersion, partition), token));
            var mcpPlacement = await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                    new AtomicPartitionPlacementReadRequest(QueueDispatchCasRf3Protocol.PlacementVersion, partition), token));
            await Assert.That(JsonDefaults.Serialize(mcpPlacement.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(placement))).IsTrue();
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(original, token));
            await QueueDispatchCasRf3Receipts.SeedAsync(receipt, original.CommandId, lane, placement);
            var outbox = await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(partition, token));
            var message = await PublicReadAssertions.MessageAsync(await sdk.InspectAsync(
                new(lane, QueueDispatchCasRf3Protocol.Message), token)) ?? throw new InvalidOperationException();
            await Assert.That(message.Metadata.State).IsEqualTo(MessageState.Ready);
            await Assert.That(message.PayloadJson).IsEqualTo(QueueDispatchCasRf3Protocol.Payload);
            await Assert.That(message.HeadersJson).IsEqualTo(QueueDispatchCasRf3Protocol.Headers);
            var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.BrowseQueueAsync(new(lane), token));
            await Assert.That(page.Items).HasSingleItem();
            return new QueueDispatchCasRf3State(lane, definition, member, original, receipt, message,
                JsonDefaults.Serialize(page.Counters), JsonDefaults.Serialize(page.Items), placement, outbox);
        }, token);
}
