using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderRf3Preparation
{
    internal static Task<QueueDeadlineLeaderRf3State> CreateAsync(ClusterFixture fixture, string subject,
        CancellationToken token)
        => QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var tenant = QueueDeadlineLeaderRf3Protocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
            var partition = new PartitionRef(tenant, QueueDeadlineLeaderRf3Protocol.Database,
                QueueDeadlineLeaderRf3Protocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            var lane = new QueueLaneRef(partition, QueueDeadlineLeaderRf3Protocol.Queue);
            var definition = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId);
            await QueueDeadlineLeaderRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(tenant, partition.DatabaseId, definition), token)), definition);
            var identity = await MessagingRf3Identity.CreateSelectedAsync(fixture, subject, tenant,
                [new(partition.DatabaseId, lane.Queue, QueueDeadlineRf3Protocol.Worker)], [], false, token);
            var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(
                new(1, partition), token));
            await QueueDeadlineLeaderRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                    new AtomicPartitionPlacementReadRequest(1, partition), token))).Value, placement);
            var baseline = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
            var state = new QueueDeadlineLeaderRf3State(lane, identity,
                TimeProvider.System.GetUtcNow().AddSeconds(QueueDeadlineRf3Protocol.LeadSeconds), placement, baseline);
            await QueueDeadlineLeaderRf3Current.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderRf3Actions.PublishAsync(admin, state, false, token);
            return state;
        }, token);
}
