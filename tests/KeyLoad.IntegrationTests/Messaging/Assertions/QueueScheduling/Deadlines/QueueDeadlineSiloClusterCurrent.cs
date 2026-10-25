using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueDeadlineSiloClusterCurrent
{
    internal static async Task ObserveAsync(RequestCqrsClusterCallers admin, QueueDeadlineSiloClusterState state,
        CancellationToken token)
    {
        await MessageAsync(admin, state, QueueDeadlineSiloClusterProtocol.Message, state.Original, token);
        await MessageAsync(admin, state, QueueDeadlineSiloClusterProtocol.Healthy, state.Healthy, token);
        var partition = state.Lane.Partition;
        var page = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListResourcesAsync(
            new(partition.TenantId, partition.DatabaseId), token));
        AdminResourceInfo[] expected = [new(state.Lane.Queue, ResourceKind.WorkQueue,
            partition.TransactionDomainId, 1L, 0, false)];
        await QueueDeadlineSiloClusterExpected.EqualAsync(page.Items.ToArray(), expected);
        await Assert.That(page.NextAfterName).IsNull();
        await QueueDeadlineSiloClusterExpected.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.OutboxStatusAsync(partition, token)), state.Baseline);
        await QueueDeadlineSiloClusterExpected.EqualAsync((await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus,
                new GetOutboxStatusRequest(partition), token))).Value, state.Baseline);
    }

    /// <summary>One committed queue mutation appends exactly one partition-local outbox entry with its effect
    /// (ChangeFeeds REQ-FEED-001); the advanced head is the exact expectation for every later observation.</summary>
    internal static async Task OutboxAdvancedAsync(RequestCqrsClusterCallers admin, QueueDeadlineSiloClusterState state,
        CancellationToken token)
    {
        var previous = state.Baseline;
        var actual = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(state.Lane.Partition, token));
        await Assert.That(actual.Head.Tail).IsEqualTo(previous.Head.Tail + 1L);
        await Assert.That(actual.Head.FirstAvailable).IsEqualTo(previous.Head.FirstAvailable);
        await Assert.That(actual.Head.StoredRecords).IsEqualTo(previous.Head.StoredRecords + 1L);
        await Assert.That(actual.Head.StoredBytes).IsGreaterThan(previous.Head.StoredBytes);
        await QueueDeadlineSiloClusterExpected.EqualAsync(actual.Consumers, previous.Consumers);
        state.Baseline = actual;
    }

    private static async Task MessageAsync(RequestCqrsClusterCallers admin, QueueDeadlineSiloClusterState state,
        string id, MessageInspection? expected, CancellationToken token)
    {
        var request = new InspectMessageRequest(state.Lane, id);
        await QueueDeadlineSiloClusterExpected.EqualAsync(await PublicReadAssertions.MessageAsync(
            await admin.Sdk.InspectAsync(request, token)), expected);
        await QueueDeadlineSiloClusterExpected.EqualAsync((await PublicReadAssertions.MessageAsync(
            await admin.Mcp.CallAsync(McpCallerTools.MessagesInspect, request, token))).Value, expected);
    }
}
