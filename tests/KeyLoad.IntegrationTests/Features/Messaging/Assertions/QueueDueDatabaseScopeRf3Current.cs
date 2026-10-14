using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDueDatabaseScopeRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers admin,
        QueueDueDatabaseScopeRf3State state, CancellationToken token)
    {
        foreach (var lane in state.Lanes)
        {
            await QueueDueDatabaseScopeRf3Expected.MessageAsync(admin, lane.Lane,
                QueueDueDatabaseScopeRf3Protocol.Message, lane.Message, token);
            await QueueDueDatabaseScopeRf3Expected.MessageAsync(admin, lane.Lane,
                QueueDueDatabaseScopeRf3Protocol.Healthy, lane.Healthy, token);
            var partition = lane.Lane.Partition;
            var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
                new(partition.TenantId, partition.DatabaseId), token));
            AdminResourceInfo[] expected = [new(QueueDueDatabaseScopeRf3Protocol.Queue,
                ResourceKind.WorkQueue, partition.TransactionDomainId, 1L, 0, false)];
            await QueueDueDatabaseScopeRf3Expected.EqualAsync(page.Items.ToArray(), expected);
            await Assert.That(page.NextAfterName).IsNull();
            await QueueDueDatabaseScopeRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token)), lane.Placement);
            await QueueDueDatabaseScopeRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
                await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                    new AtomicPartitionPlacementReadRequest(1, partition), token))).Value, lane.Placement);
            await QueueDueDatabaseScopeRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.OutboxStatusAsync(partition, token)), lane.Outbox);
            await QueueDueDatabaseScopeRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
                await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                    new GetOutboxStatusRequest(partition), token))).Value, lane.Outbox);
        }
    }
}
