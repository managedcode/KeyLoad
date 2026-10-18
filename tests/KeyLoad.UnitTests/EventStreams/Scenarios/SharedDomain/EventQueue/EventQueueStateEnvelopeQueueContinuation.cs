using KeyLoad.Core;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeQueueContinuation
{
    private const string PutMutation = "putDocument";
    private const string InboxFamily = "inbox";
    private const string RowSeparator = ":";
    private const string MissingReceive = "The original native receive is absent from its retained history.";

    internal static async Task ClaimAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeSample sample,
        bool scheduled)
    {
        var id = Guid.NewGuid();
        var time = scheduled ? fixture.ScheduledAt : fixture.At;
        var message = scheduled ? Scheduled : Item;
        var request = new ReceiveRequest(id, fixture.Lane, MaxMessages: SingleResult, LeaseSeconds: LeaseSeconds);
        var claims = new DeliveryClaims(fixture.Lane, message, Root, FirstLeaseVersion, InitialGeneration,
            fixture.Store.Identity.Incarnation);
        var token = fixture.Database.Sign(claims);
        var expected = new ReceiveResult(id,
            [new(message, Payload, Headers, token, FirstLeaseVersion, time.AddSeconds(LeaseSeconds), FirstAttempt, InitialGeneration)],
            NextToken(fixture));
        var entry = await SubmitAsync(fixture, sample, OperationKind.Receive, id, request, time, expected);
        var received = entry.Result.Get<ReceiveResult>();
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Database.Verify<DeliveryClaims>(
            received.Deliveries.Single().Token), claims);
        if (scheduled)
        {
            sample.ScheduledClaim = received;
        }
        else
        {
            sample.ReadyClaim = received;
        }
    }

    internal static async Task ProcessAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeSample sample)
    {
        var claim = sample.ReadyClaim ?? throw new InvalidOperationException(MissingReceive);
        var id = Guid.NewGuid();
        var request = new ProcessingRequest(id, fixture.Lane, claim.Deliveries.Single().Token, Handler, InitialGeneration,
            [new PutDocument(Documents, Processed, Payload, NoUsage)]);
        var expected = new CommitReceipt(id, NextToken(fixture), [new(PutMutation, Documents, Processed, InitialRevision), new(nameof(DeliveryAction.Ack), Queue, Item, ReadyAckVersion)], DurabilityProfile.ProcessDurable, 1);
        _ = await SubmitAsync(fixture, sample, OperationKind.Processing, id, request, fixture.At, expected);
        sample.InboxBytes = Required(fixture, InboxKey(fixture));
        sample.Processing = request;
        sample.ProcessingReceipt = expected;
        ConsumeReceive(sample, claim.RequestId);
    }

    internal static async Task CompleteScheduledAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeSample sample)
    {
        var claim = sample.ScheduledClaim ?? throw new InvalidOperationException(MissingReceive);
        var id = Guid.NewGuid();
        var request = new DeliveryCommand(id, fixture.Lane, claim.Deliveries.Single().Token, DeliveryAction.Ack);
        var expected = new CommitReceipt(id, NextToken(fixture), [new(nameof(DeliveryAction.Ack), Queue, Scheduled, ScheduledAckVersion)], DurabilityProfile.ProcessDurable, 1);
        _ = await SubmitAsync(fixture, sample, OperationKind.Delivery, id, request, fixture.ScheduledAt, expected);
        ConsumeReceive(sample, claim.RequestId);
    }

    internal static async Task DuplicateProcessingAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeSample sample)
    {
        var request = (sample.Processing ?? throw new InvalidOperationException(MissingReceive)) with
        {
            CommandId = Guid.NewGuid()
        };
        var expected = sample.ProcessingReceipt ?? throw new InvalidOperationException(MissingReceive);
        var before = Domain(fixture, request.CommandId);
        var expectedPosition = checked(fixture.Store.Position + CommitPositionStep);
        _ = await SubmitAsync(fixture, sample, OperationKind.Processing, request.CommandId, request, fixture.ScheduledAt, expected);
        await Assert.That(fixture.Store.Position).IsEqualTo(expectedPosition);
        await Assert.That(KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Clock(fixture.Store)).IsEqualTo(fixture.ScheduledAt);
        await Assert.That(Domain(fixture, request.CommandId)).IsEquivalentTo(before,
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await EventQueueStateEnvelopeHistoryAssertions.InboxAsync(fixture, sample);
    }

    internal static byte[] InboxKey(EventQueueStateEnvelopeFixture fixture)
    {
        return KeySpace.Partition(InboxFamily, fixture.Partition, Queue, Item, Handler, InitialGeneration);
    }

    private static string[] Domain(EventQueueStateEnvelopeFixture fixture, Guid id)
    {
        var outcome = Convert.ToHexString(KeySpace.PartitionOutcome(fixture.Partition, Root, id)) + RowSeparator;
        var locator = Convert.ToHexString(KeySpace.OutcomeLocatorV2(fixture.Partition, Root, id)) + RowSeparator;
        var clock = Convert.ToHexString(KeySpace.Clock.Span) + RowSeparator;
        return KeyLoad.UnitTests.Features.Messaging.QueueWholeFlowStorage.Bytes(fixture.Store)
            .Where(row => !row.StartsWith(outcome, StringComparison.Ordinal)
                && !row.StartsWith(locator, StringComparison.Ordinal)
                && !row.StartsWith(clock, StringComparison.Ordinal)).ToArray();
    }

    internal static void ConsumeReceive(EventQueueStateEnvelopeSample sample, Guid id)
    {
        var entry = sample.History.Single(value => value.Operation.CommandId == id);
        var index = sample.History.IndexOf(entry);
        sample.History[index] = entry with
        {
            ConsumedReceive = true
        };
    }
}
