using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveReceive
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        var request = new ReceiveSubscriptionRequest(Guid.NewGuid(), state.Group, MaxEvents: UnacknowledgedArchiveProtocol.ClaimedEvents,
            LeaseSeconds: new SubscriptionPolicy().MaxLeaseSeconds);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReceiveSubscriptionAsync(request, token));
        state.Issued = UnacknowledgedArchiveProtocol.ThirdPosition;
        await Assert.That(result.RequestId).IsEqualTo(request.RequestId);
        await SqlRf3Protocol.EqualAsync(UnacknowledgedArchiveCurrent.Expected(state), result.Status);
        await Assert.That(result.Deliveries.Length).IsEqualTo(UnacknowledgedArchiveProtocol.ClaimedEvents);
        await Assert.That(result.Token.AtomicPartitionId).IsEqualTo(state.Original.Partition.AtomicPartitionId);
        await Assert.That(result.Token.Incarnation).IsEqualTo(state.Placement.Incarnation);
        await Assert.That(result.Token.OwnershipEpoch).IsEqualTo(state.Placement.PlacementEpoch);
        await Assert.That(result.Token.Position).IsGreaterThan(UnacknowledgedArchiveProtocol.PositivePositionBoundary);
        foreach (var delivery in result.Deliveries)
        {
            await Assert.That(delivery.Token).IsNotEmpty();
            await Assert.That(delivery.LeaseUntil).IsNotEqualTo(default(DateTimeOffset));
        }
        await SqlRf3Protocol.EqualAsync(result with
        {
            Deliveries = result.Deliveries.Select((row, i) =>
            row with
            {
                Event = state.Rows[i],
                Attempt = UnacknowledgedArchiveProtocol.FirstAttempt,
                LeaseVersion = UnacknowledgedArchiveProtocol.OriginalLeaseVersion
            }).ToImmutableArray()
        }, result);
        state.Receive = request;
        state.Received = result;
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.SuccessAsync(callers, state, McpCallerTools.SubscriptionsReceive,
            request, result, () => callers.Sdk.ReceiveSubscriptionAsync(request, token), token);
    }
}
