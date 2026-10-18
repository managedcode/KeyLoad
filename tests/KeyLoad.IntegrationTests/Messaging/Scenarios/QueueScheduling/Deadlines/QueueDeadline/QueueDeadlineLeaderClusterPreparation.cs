using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderClusterPreparation
{
    internal static Task<QueueDeadlineLeaderClusterState> CreateAsync(ClusterFixture fixture, string subject,
        CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var tenant = QueueDeadlineLeaderClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
            var partition = new PartitionRef(tenant, QueueDeadlineLeaderClusterProtocol.Database,
                QueueDeadlineLeaderClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            var lane = new QueueLaneRef(partition, QueueDeadlineLeaderClusterProtocol.Queue);
            var definition = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId);
            await QueueDeadlineLeaderClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(tenant, partition.DatabaseId, definition), token)), definition);
            var identity = await MessagingClusterIdentity.CreateSelectedAsync(fixture, subject, tenant,
                [new(partition.DatabaseId, lane.Queue, QueueDeadlineClusterProtocol.Worker)], [], false, token);
            var baseline = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
            var state = new QueueDeadlineLeaderClusterState(lane, identity,
                TimeProvider.System.GetUtcNow().AddSeconds(QueueDeadlineClusterProtocol.LeadSeconds), baseline);
            await QueueDeadlineLeaderClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineLeaderClusterActions.PublishAsync(admin, state, false, token);
            return state;
        }, token);
}
