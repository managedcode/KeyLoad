using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDueDatabaseScopeClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers admin,
        QueueDueDatabaseScopeClusterState state, CancellationToken token)
    {
        foreach (var lane in state.Lanes)
        {
            await QueueDueDatabaseScopeClusterExpected.MessageAsync(admin, lane.Lane,
                QueueDueDatabaseScopeClusterProtocol.Message, lane.Message, token);
            await QueueDueDatabaseScopeClusterExpected.MessageAsync(admin, lane.Lane,
                QueueDueDatabaseScopeClusterProtocol.Healthy, lane.Healthy, token);
            var partition = lane.Lane.Partition;
            var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
                new(partition.TenantId, partition.DatabaseId), token));
            AdminResourceInfo[] expected = [new(QueueDueDatabaseScopeClusterProtocol.Queue,
                ResourceKind.WorkQueue, partition.TransactionDomainId, 1L, 0, false)];
            await QueueDueDatabaseScopeClusterExpected.EqualAsync(page.Items.ToArray(), expected);
            await Assert.That(page.NextAfterName).IsNull();
            await QueueDueDatabaseScopeClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.OutboxStatusAsync(partition, token)), lane.Outbox);
            await QueueDueDatabaseScopeClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
                await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                    new GetOutboxStatusRequest(partition), token))).Value, lane.Outbox);
        }
    }
}
