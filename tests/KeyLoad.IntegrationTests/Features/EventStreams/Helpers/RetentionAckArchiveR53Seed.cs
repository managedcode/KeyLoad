using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class RetentionAckArchiveR53Seed
{
    internal static async Task BeginAsync(RequestCqrsRf3Callers callers,
        ClusterRestoreMixedRetentionRf3State original, CancellationToken token)
    {
        var state = new RetentionAckArchiveR53State(original);
        original.AcknowledgedRetention = state;
        state.Page = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(new(original.PurgedSource), token));
        await ClusterRestoreMixedRetentionRf3Literal.PageAsync(state.Page, original.PurgedSource,
            ClusterRestoreMixedRetentionRf3PurgeTopic.Events(), 1, 1);
        state.Configure = new(Guid.NewGuid(), state.Group, state.Definition);
        state.Configured = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureSubscriptionAsync(state.Configure, token));
        await SqlRf3Protocol.EqualAsync(RetentionAckArchiveR53Current.Expected(state), state.Configured);
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.Configured, (await McpCallerAssertions.SuccessAsync<SubscriptionInfo>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsConfigure, state.Configure, token))).Value);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        state.Claim = new(Guid.NewGuid(), state.Group, MaxEvents: 3, LeaseSeconds: new SubscriptionPolicy().MaxLeaseSeconds);
        state.Claimed = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveSubscriptionAsync(state.Claim, token));
        state.Issued = 3;
        await Assert.That(state.Claimed.RequestId).IsEqualTo(state.Claim.RequestId);
        await SqlRf3Protocol.EqualAsync(RetentionAckArchiveR53Current.Expected(state), state.Claimed.Status);
        await Assert.That(state.Claimed.Deliveries.Length).IsEqualTo(3);
        for (var i = 0; i < 3; i++)
        {
            var delivery = state.Claimed.Deliveries[i];
            await Assert.That(delivery.Token).IsNotEmpty();
            await Assert.That(delivery.LeaseUntil).IsNotEqualTo(default(DateTimeOffset));
        }
        var expected = state.Claimed with
        {
            Deliveries = state.Claimed.Deliveries.Select((delivery, i) =>
            delivery with { Event = state.Page.Events[i], Attempt = 1, LeaseVersion = 1 }).ToImmutableArray()
        };
        await SqlRf3Protocol.EqualAsync(expected, state.Claimed);
        await Assert.That(state.Claimed.Token.AtomicPartitionId).IsEqualTo(original.Partition.AtomicPartitionId);
        await Assert.That(state.Claimed.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(state.Claimed.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(state.Claimed.Token.Position).IsGreaterThan(0L);
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await SqlRf3Protocol.EqualAsync(state.Claimed, (await McpCallerAssertions.SuccessAsync<ReceiveSubscriptionResult>(
            await callers.Mcp.CallAsync(McpCallerTools.SubscriptionsReceive, state.Claim, token))).Value);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await RetentionAckArchiveR53Actions.AckAsync(callers, state, 2, 0, token);
        await RetentionAckArchiveR53Actions.AckAsync(callers, state, 0, 1, token);
        await RetentionAckArchiveR53Actions.PauseAsync(callers, state, true, token);
        state.Refused = new(Guid.NewGuid(), original.Partition, [new PurgeTopic(original.PurgedSource.Resource, 2)]);
        await RetentionAckArchiveR53Actions.RefusedAsync(callers, state, true, token);
        await RetentionAckArchiveR53Actions.PauseAsync(callers, state, false, token);
        await RetentionAckArchiveR53Actions.AckAsync(callers, state, 1, 3, token);
        await RetentionAckArchiveR53Actions.PauseAsync(callers, state, true, token);
    }

    internal static async Task FinishedAsync(RequestCqrsRf3Callers callers,
        ClusterRestoreMixedRetentionRf3State original, CancellationToken token)
    {
        var state = original.AcknowledgedRetention ?? throw new InvalidOperationException(RetentionAckArchiveR53Protocol.Missing);
        state.Retained = true;
        state.Page = original.PurgedPage;
        await RetentionAckArchiveR53Current.CaptureAsync(callers, state, token);
        await RetentionAckArchiveR53Current.RunAsync(callers, state, token);
        await RetentionAckArchiveR53Replay.SourceAsync(callers, state, token);
    }
}
