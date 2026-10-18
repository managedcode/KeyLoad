using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDeadlineLeaderClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers admin, QueueDeadlineLeaderClusterState state,
        CancellationToken token)
    {
        await MessageAsync(admin, state, QueueDeadlineLeaderClusterProtocol.Message, state.Original, token);
        await MessageAsync(admin, state, QueueDeadlineLeaderClusterProtocol.Healthy, state.Healthy, token);
        var partition = state.Lane.Partition;
        var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(partition.TenantId, partition.DatabaseId), token));
        AdminResourceInfo[] expected = [new(state.Lane.Queue, ResourceKind.WorkQueue,
            partition.TransactionDomainId, 1L, 0, false)];
        await QueueDeadlineLeaderClusterExpected.EqualAsync(page.Items.ToArray(), expected);
        await Assert.That(page.NextAfterName).IsNull();
        await QueueDeadlineLeaderClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.OutboxStatusAsync(partition, token)), state.Baseline);
        await QueueDeadlineLeaderClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(partition), token))).Value, state.Baseline);
    }

    private static async Task MessageAsync(RequestCqrsClusterCallers admin, QueueDeadlineLeaderClusterState state,
        string id, MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(state.Lane, id);
        await QueueDeadlineLeaderClusterExpected.EqualAsync(await PublicReadAssertions.MessageAsync(
            await admin.Sdk.InspectAsync(request, token)), expected);
        await QueueDeadlineLeaderClusterExpected.EqualAsync((await PublicReadAssertions.MessageAsync(
            await admin.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }
}
