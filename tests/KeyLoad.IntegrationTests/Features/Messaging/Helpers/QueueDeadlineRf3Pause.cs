using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineRf3Pause
{
    internal static async Task SetAsync(ClusterFixture fixture, QueueLaneRef lane, bool paused, CancellationToken token)
    {
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node1, fixture.AdminKey, async admin =>
        {
            await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(lane.Partition.TenantId, lane.Partition.DatabaseId, new ResourceDefinition(lane.Queue,
                    ResourceKind.WorkQueue, lane.Partition.TransactionDomainId)
                { Paused = paused }), token));
            return true;
        }, token);
    }

    internal static async Task RefusedAsync(ClusterFixture fixture, QueueLaneRef lane, MessagingRf3Identity identity,
        MessageInspection scheduled, CancellationToken token)
    {
        var condition = new AdvanceQueueDeadline(lane.Queue, scheduled.Metadata.Id, scheduled.Metadata.State,
            scheduled.Metadata.StateVersion, scheduled.Metadata.LeaseVersion, scheduled.Metadata.NotBefore!.Value,
            QueueDeadlineKind.PromoteScheduled);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [condition]);
        await QueueDeadlineRf3Callers.UseAsync(fixture, McpCallerProtocol.Node2, identity.Secret, async worker =>
        {
            await QueueLeaseRf3Assertions.DeniedAsync(await worker.Sdk.CommitAsync(command, token), ErrorCode.DispatchPaused);
            await McpCallerAssertions.ErrorAsync(await worker.Mcp.CallAsync(
                McpCallerTools.DocumentsCommit, command, token), ErrorCode.DispatchPaused, dispatched: true);
            var sql = SqlRf3Protocol.Call(lane.Partition, McpCallerTools.DocumentsCommit, command);
            await QueueLeaseRf3Assertions.DeniedAsync(await worker.Sdk.ExecuteSqlAsync(sql, token), ErrorCode.DispatchPaused);
            await McpCallerAssertions.ErrorAsync(await worker.Mcp.CallAsync(
                SqlOperationProtocol.ToolName, sql, token), ErrorCode.DispatchPaused, dispatched: true);
            await QueueDeadlineRf3Assertions.RequireAsync(worker, lane, scheduled, token);
            return true;
        }, token);
    }
}
