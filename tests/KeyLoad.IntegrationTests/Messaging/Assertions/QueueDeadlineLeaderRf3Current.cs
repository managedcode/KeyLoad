using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderRf3Current
{
    internal static async Task ObserveAsync(RequestCqrsRf3Callers admin, QueueDeadlineLeaderRf3State state,
        CancellationToken token)
    {
        await MessageAsync(admin, state, QueueDeadlineLeaderRf3Protocol.Message, state.Original, token);
        await MessageAsync(admin, state, QueueDeadlineLeaderRf3Protocol.Healthy, state.Healthy, token);
        var partition = state.Lane.Partition;
        var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(partition.TenantId, partition.DatabaseId), token));
        AdminResourceInfo[] expected = [new(state.Lane.Queue, ResourceKind.WorkQueue,
            partition.TransactionDomainId, 1L, 0, false)];
        await QueueDeadlineLeaderRf3Expected.EqualAsync(page.Items.ToArray(), expected);
        await Assert.That(page.NextAfterName).IsNull();
        await QueueDeadlineLeaderRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token)), state.Placement);
        await QueueDeadlineLeaderRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead,
                new AtomicPartitionPlacementReadRequest(1, partition), token))).Value, state.Placement);
        await QueueDeadlineLeaderRf3Expected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.OutboxStatusAsync(partition, token)), state.Baseline);
        await QueueDeadlineLeaderRf3Expected.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(partition), token))).Value, state.Baseline);
    }

    private static async Task MessageAsync(RequestCqrsRf3Callers admin, QueueDeadlineLeaderRf3State state,
        string id, MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(state.Lane, id);
        await QueueDeadlineLeaderRf3Expected.EqualAsync(await PublicReadAssertions.MessageAsync(
            await admin.Sdk.InspectAsync(request, token)), expected);
        await QueueDeadlineLeaderRf3Expected.EqualAsync((await PublicReadAssertions.MessageAsync(
            await admin.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }
}
