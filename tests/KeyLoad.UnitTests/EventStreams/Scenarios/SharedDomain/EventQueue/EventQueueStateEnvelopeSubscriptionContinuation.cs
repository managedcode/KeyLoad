using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeSubscriptionContinuation
{
    private const string DeliveryPurpose = "subscription-delivery";
    private const string FirstPositionIdentity = "1";
    private const string MissingClaim = "The original execution subscription claim is unavailable.";

    internal static async Task ConfigureAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state)
    {
        var id = Guid.NewGuid();
        var request = new ConfigureSubscriptionRequest(id, fixture.Subscription, fixture.Definition);
        var expected = new SubscriptionInfo(fixture.Subscription, fixture.Definition, InitialGeneration,
            InitialOwnershipEpoch, NoUsage, NoUsage, InitialRevision, false, null);
        _ = await SubmitAsync(fixture, state, OperationKind.ConfigureSubscription, id, request, fixture.At, expected);
    }

    internal static async Task ClaimAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state)
    {
        var id = Guid.NewGuid();
        var request = new ReceiveSubscriptionRequest(id, fixture.Subscription, MaxEvents: SingleResult, LeaseSeconds: LeaseSeconds);
        var claims = new GroupClaims(DeliveryPurpose, fixture.Store.Identity.Incarnation, fixture.Subscription,
            InitialGeneration, InitialOwnershipEpoch, InitialRevision, FirstLeaseVersion, Root, InitialPolicyEpoch, InitialPolicyEpoch);
        var data = new SourceEventRecord(fixture.TopicSource, InitialRevision, TopicEventSequence,
            new(Item, EventType, Payload, Headers), fixture.At);
        var expected = new ReceiveSubscriptionResult(id,
            [new(data, fixture.Database.Sign(claims), FirstLeaseVersion, fixture.At.AddSeconds(LeaseSeconds), FirstAttempt)],
            new(fixture.Subscription, fixture.Definition, InitialGeneration, InitialOwnershipEpoch, NoUsage,
                InitialRevision, InitialRevision, false, null), NextToken(fixture));
        var entry = await SubmitAsync(fixture, state, OperationKind.ReceiveSubscription, id, request, fixture.At, expected);
        state.GroupClaim = entry.Result.Get<ReceiveSubscriptionResult>();
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(
            fixture.Database.Verify<GroupClaims>(state.GroupClaim.Deliveries.Single().Token), claims);
    }

    internal static async Task CompleteAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state)
    {
        var claim = state.GroupClaim ?? throw new InvalidOperationException(MissingClaim);
        var id = Guid.NewGuid();
        var request = new SubscriptionDeliveryCommand(id, fixture.Subscription, claim.Deliveries.Single().Token, DeliveryAction.Ack);
        var expected = new CommitReceipt(id, NextToken(fixture), [new(nameof(DeliveryAction.Ack), Topic, FirstPositionIdentity, FirstLeaseVersion)], DurabilityProfile.ProcessDurable, 1);
        _ = await SubmitAsync(fixture, state, OperationKind.SubscriptionDelivery, id, request, fixture.At, expected);
        EventQueueStateEnvelopeQueueContinuation.ConsumeReceive(state, claim.RequestId);
    }
}
