using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDeadlineSiloClusterPreparation
{
    internal static Task<QueueDeadlineSiloClusterState> CreateAsync(ClusterFixture fixture, string subject,
        CancellationToken token)
        => QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var tenant = QueueDeadlineSiloClusterProtocol.TenantPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
            var partition = new PartitionRef(tenant, QueueDeadlineSiloClusterProtocol.Database,
                QueueDeadlineSiloClusterProtocol.Domain, Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat));
            var lane = new QueueLaneRef(partition, QueueDeadlineSiloClusterProtocol.Queue);
            var definition = new ResourceDefinition(lane.Queue, ResourceKind.WorkQueue, partition.TransactionDomainId);
            await QueueDeadlineSiloClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(tenant, partition.DatabaseId, definition), token)), definition);
            var identity = await MessagingClusterIdentity.CreateSelectedAsync(fixture, subject, tenant,
                [new(partition.DatabaseId, lane.Queue, QueueDeadlineClusterProtocol.Worker)], [], false, token);
            var baseline = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
            var state = new QueueDeadlineSiloClusterState(lane, identity,
                TimeProvider.System.GetUtcNow().AddSeconds(QueueDeadlineClusterProtocol.LeadSeconds), baseline);
            await QueueDeadlineSiloClusterCurrent.ObserveAsync(admin, state, token);
            await QueueDeadlineSiloClusterActions.PublishAsync(admin, state, false, token);
            return state;
        }, token);
}
