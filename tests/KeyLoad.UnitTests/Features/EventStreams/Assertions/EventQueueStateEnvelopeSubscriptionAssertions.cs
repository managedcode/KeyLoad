using KeyLoad.Core;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeSubscriptionAssertions
{
    private const string StateFamily = "subscription";
    private const string WindowFamily = "subscription-window";
    private const string CompletionFamily = "subscription-completion";
    private const string InboxFamily = "subscription-inbox";

    internal static async Task CurrentAsync(EventQueueStateEnvelopeFixture fixture, bool completed)
    {
        var checkpoint = completed ? InitialRevision : NoUsage;
        var expected = new GroupState(fixture.Definition, InitialGeneration, InitialOwnershipEpoch, checkpoint, InitialRevision);
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Store.Read(view => view.GetRecord<GroupState>(
            Key(fixture, StateFamily))), expected);
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Database.GetSubscription(Root, fixture.Subscription),
            new SubscriptionInfo(fixture.Subscription, fixture.Definition, InitialGeneration, InitialOwnershipEpoch,
                checkpoint, InitialRevision, InitialRevision, false, null));
        if (completed)
        {
            await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Store.Read(view => view.GetRecord<SubscriptionCompletion>(
                Key(fixture, CompletionFamily, InitialGeneration, InitialRevision))),
                new SubscriptionCompletion(InitialGeneration, InitialRevision, nameof(GroupDeliveryState.Acked), InitialPolicyEpoch));
        }
        else
        {
            await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Store.Read(view => view.GetRecord<GroupDelivery>(
                Key(fixture, WindowFamily, InitialGeneration, InitialRevision))), new GroupDelivery(InitialRevision,
                    GroupDeliveryState.Leased, FirstAttempt, FirstLeaseVersion, LeaseUntil: fixture.At.AddSeconds(LeaseSeconds),
                    PrincipalId: Root));
        }
        foreach (var family in new[] { StateFamily, WindowFamily, CompletionFamily, InboxFamily })
        {
            var page = fixture.Store.Read(view => view.Scan(Key(fixture, family), ScanBound));
            await Assert.That(page.HasMore).IsFalse();
            var count = family == StateFamily || family == (completed ? CompletionFamily : WindowFamily) ? SingleResult : NoUsage;
            await Assert.That(page.Records.Length).IsEqualTo(count);
        }
    }

    internal static byte[] Key(EventQueueStateEnvelopeFixture fixture, string family, params object?[] suffix)
    {
        return KeySpace.Partition(family, fixture.Partition,
            new object?[] { Topic, nameof(EventSourceKind.Topic), null, InitialGeneration, Group }.Concat(suffix).ToArray());
    }
}
