using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.Messaging;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveCurrent
{
    internal static SubscriptionInfo Expected(UnacknowledgedArchiveState state)
        => new(state.Group, state.Definition, state.Generation, state.Generation, state.Checkpoint,
            state.Issued, state.Tail, state.Paused, null);

    internal static async Task CaptureAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        var request = new AtomicPartitionPlacementReadRequest(UnacknowledgedArchiveProtocol.Version, state.Original.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadAtomicPartitionPlacementAsync(request, token));
        if (state.Placement is not null && state.Placement.Incarnation == state.Original.CurrentTargetOwner.Incarnation)
        {
            await SqlRf3Protocol.EqualAsync(state.Placement, placement);
        }
        await SqlRf3Protocol.EqualAsync(state.Original.CurrentTargetOwner.PhysicalShardId, placement.PhysicalShardId);
        await SqlRf3Protocol.EqualAsync(state.Original.CurrentTargetOwner.Incarnation, placement.Incarnation);
        await SqlRf3Protocol.EqualAsync(state.Original.CurrentTargetOwner.VoterIds, placement.VoterIds);
        state.Placement = placement;
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.OutboxStatusAsync(state.Original.Partition, token));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await callers.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Original.Partition), token))).Value);
    }

    internal static async Task RunAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        if (state.HasGroup)
        {
            await SqlRf3Protocol.EqualAsync(Expected(state), await McpCallerAssertions.SdkSuccessAsync(
                await callers.Sdk.SubscriptionStatusAsync(state.Group, token)));
            await SqlRf3Protocol.EqualAsync(Expected(state), (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
                await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsStatus, new GetSubscriptionRequest(state.Group), token))).Value);
        }
        var read = new ReadEventSourceRequest(state.Source, AfterPosition: state.First - UnacknowledgedArchiveProtocol.FirstPosition);
        await PageAsync(state, await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(read, token)));
        await PageAsync(state, (await McpCallerAssertions.SuccessAsync<EventSourcePage>(
            await callers.Mcp.CallAsync(McpCallerTools.EventsRead, read, token))).Value);
        await ClusterRestoreMixedRetentionRf3Literal.TopicsAsync(callers, state.Original, token);
        await ClusterRestoreMixedRetentionRf3Literal.InboxAsync(callers, state.Original, token);
        if (state.Original.Continued)
        {
            await ClusterRestoreMixedRetentionRf3Queue.HealthyAsync(callers, state.Original, token);
        }
        else
        {
            await QueueLifecyclePublicAssertions.InitialAsync(callers, state.Original.OriginalQueue, token);
        }
        var request = new AtomicPartitionPlacementReadRequest(UnacknowledgedArchiveProtocol.Version, state.Original.Partition);
        await SqlRf3Protocol.EqualAsync(state.Placement, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.ReadAtomicPartitionPlacementAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(state.Placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await callers.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, request, token))).Value);
        await SqlRf3Protocol.EqualAsync(state.Outbox, await McpCallerAssertions.SdkSuccessAsync(
            await callers.Sdk.OutboxStatusAsync(state.Original.Partition, token)));
        await SqlRf3Protocol.EqualAsync(state.Outbox, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await callers.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(state.Original.Partition), token))).Value);
    }

    private static async Task PageAsync(UnacknowledgedArchiveState state, EventSourcePage page)
    {
        await SqlRf3Protocol.EqualAsync(state.Source, page.Source);
        await SqlRf3Protocol.EqualAsync(new EventSourceHead(state.Tail, state.First, UnacknowledgedArchiveProtocol.InitialGeneration), page.Head);
        await Assert.That(page.Cursor).IsNotEmpty();
        await Assert.That(page.CutPosition).IsGreaterThan(UnacknowledgedArchiveProtocol.PositivePositionBoundary);
        await Assert.That(page.HasMore).IsFalse();
        await SqlRf3Protocol.EqualAsync(state.Rows, page.Events);
    }

    internal static async Task ReceiptAsync(UnacknowledgedArchiveState state, CommitReceipt receipt,
        Guid id, MutationReceipt expected)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(id);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(state.Original.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(UnacknowledgedArchiveProtocol.PositivePositionBoundary);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(new[] { expected }, receipt.Mutations.ToArray());
    }
}
