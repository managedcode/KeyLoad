using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class RetentionAckArchiveR53Current
{
    internal static SubscriptionInfo Expected(RetentionAckArchiveR53State state)
        => new(state.Group, state.Definition, 1, 1, state.Checkpoint, state.Issued, 3, state.Paused, null);

    internal static async Task CaptureAsync(RequestCqrsRf3Callers callers, RetentionAckArchiveR53State state,
        CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(1, state.Original.Partition);
        state.Placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OutboxStatusAsync(state.Original.Partition, token));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await callers.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Original.Partition), token))).Value);
    }

    internal static async Task RunAsync(RequestCqrsRf3Callers callers, RetentionAckArchiveR53State state,
        CancellationToken token)
    {
        var expected = Expected(state);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.SubscriptionStatusAsync(state.Group, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(state.Group), token))).Value);
        var primary = state.Original.PurgeInfo ?? new SubscriptionInfo(state.Original.PurgeGroup, new(state.Original.Principal.Id), 1, 1, 0, 0, 3, false, null);
        await SqlRf3Protocol.EqualAsync(primary, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.SubscriptionStatusAsync(state.Original.PurgeGroup, token)));
        await SqlRf3Protocol.EqualAsync(primary, (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(state.Original.PurgeGroup), token))).Value);
        var read = new ReadEventSourceRequest(state.Original.PurgedSource, AfterPosition: state.Retained ? 2 : 0);
        await PageAsync(state, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(read, token)));
        await PageAsync(state, (await McpCallerAssertions.SuccessAsync<EventSourcePage>(
            await callers.Mcp.CallAsync(McpCallerTools.EventsRead, read, token))).Value);
        if (state.Original.Continued)
        { await ClusterRestoreMixedRetentionRf3Queue.HealthyAsync(callers, state.Original, token); }
        else
        { await QueueLifecyclePublicAssertions.InitialAsync(callers, state.Original.OriginalQueue, token); }
        var placement = new AtomicPartitionPlacementReadRequest(1, state.Original.Partition);
        await SqlRf3Protocol.EqualAsync(state.Placement, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadAtomicPartitionPlacementAsync(placement, token)));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placement, token))).Value);
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.OutboxStatusAsync(state.Original.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await callers.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Original.Partition), token))).Value);
    }

    private static async Task PageAsync(RetentionAckArchiveR53State state, EventSourcePage page)
    {
        await Assert.That(page.Cursor).IsNotEmpty();
        await Assert.That(page.CutPosition).IsGreaterThan(0L);
        await SqlRf3Protocol.EqualAsync(state.Page with { Cursor = page.Cursor, CutPosition = page.CutPosition }, page);
    }

    internal static async Task ReceiptAsync(RetentionAckArchiveR53State state, CommitReceipt receipt,
        Guid commandId, MutationReceipt expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(commandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Original.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var mutation = await Assert.That(receipt.Mutations).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(expected, mutation);
    }
}
