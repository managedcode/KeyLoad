using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineClusterPause
{
    internal static async Task SetAsync(ClusterFixture fixture, QueueLaneRef lane, bool paused, CancellationToken token)
    {
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
                new(lane.Partition.TenantId, lane.Partition.DatabaseId), token));
            var current = page.Items.Single(item => string.Equals(item.Name, lane.Queue, StringComparison.Ordinal));
            await McpCallerAssertions.SdkSuccessAsync(await global::KeyLoad.Client.QueueDispatchClient.SetQueueDispatchAsync(admin.Sdk, Guid.NewGuid(),
                new(lane.Partition.TenantId, lane.Partition.DatabaseId, lane.Queue, current.SchemaVersion, paused), token));
            return true;
        }, token);
    }

    internal static async Task RefusedAsync(ClusterFixture fixture, QueueLaneRef lane, MessagingClusterIdentity identity,
        MessageInspection scheduled, CancellationToken token)
    {
        var condition = new AdvanceQueueDeadline(lane.Queue, scheduled.Metadata.Id, scheduled.Metadata.State,
            scheduled.Metadata.StateVersion, scheduled.Metadata.LeaseVersion, scheduled.Metadata.NotBefore!.Value,
            QueueDeadlineKind.PromoteScheduled);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [condition]);
        await QueueDeadlineClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, identity.Secret, async worker =>
        {
            await QueueLeaseClusterAssertions.DeniedAsync(await worker.Sdk.CommitAsync(command, token), ErrorCode.DispatchPaused);
            await McpCallerAssertions.ErrorAsync(await worker.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, token), ErrorCode.DispatchPaused, dispatched: true);
            var sql = SqlClusterProtocol.Call(lane.Partition, McpCallerTools.DocumentsCommit, command);
            await QueueLeaseClusterAssertions.DeniedAsync(await worker.Sdk.ExecuteSqlAsync(sql, token), ErrorCode.DispatchPaused);
            await McpCallerAssertions.ErrorAsync(await worker.Mcp.CallAsync(
                SqlOperationProtocol.ToolName, sql, token), ErrorCode.DispatchPaused, dispatched: true);
            await QueueDeadlineClusterAssertions.RequireAsync(worker, lane, scheduled, token);
            return true;
        }, token);
    }
}
