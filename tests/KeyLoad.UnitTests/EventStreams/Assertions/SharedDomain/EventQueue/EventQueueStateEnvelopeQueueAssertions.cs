using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeQueueAssertions
{
    private const string MetadataFamily = "message-meta";
    private const string ReadyFamily = "ready";
    private const string ScheduledFamily = "scheduled";
    private const string LeaseFamily = "lease";
    private const string CountersFamily = "queue-counters";
    private const string InboxFamily = "inbox";
    private const string DeadLetterFamily = "dead-letter";
    private const string PendingFamily = "queue-pending-dead-letter";
    private const string ParkedFamily = "queue-dead-letter-order";
    private const string OrderFamily = "queue-order";

    internal static async Task CurrentAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state,
        EventQueueStateEnvelopeStage stage)
    {
        var ready = Ready(stage, fixture.At);
        var scheduled = ScheduledMetadata(stage, fixture);
        var storedBytes = await MessageAsync(fixture, state, ready) + await MessageAsync(fixture, state, scheduled);
        var retained = (ready.State == MessageState.Acked ? NoUsage : SingleResult)
            + (scheduled.State == MessageState.Acked ? NoUsage : SingleResult);
        var leased = ready.State == MessageState.Leased || scheduled.State == MessageState.Leased;
        var active = ready.State == MessageState.Leased ? Item : Scheduled;
        var inFlightBytes = leased ? state.Bodies[Convert.ToHexString(
            KeySpace.Partition(BodyFamily, fixture.Partition, Queue, active))].LongLength : NoUsage;
        var sequence = stage >= EventQueueStateEnvelopeStage.ScheduledClaimed ? ScheduledReadySequence : ReadySequence;
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition(CountersFamily, fixture.Partition, Queue))), new QueueCounters(retained, storedBytes,
                leased ? SingleResult : NoUsage, inFlightBytes, sequence));
        await IndexesAsync(fixture, ready, scheduled, stage, retained);
    }

    private static MessageMetadata Ready(EventQueueStateEnvelopeStage stage, DateTimeOffset time)
    {
        return stage switch
        {
            EventQueueStateEnvelopeStage.Produced => new(Item, MessageState.Ready, NoUsage, InitialStateVersion,
                ReadySequence, null, null),
            EventQueueStateEnvelopeStage.Claimed => new(Item, MessageState.Leased, FirstAttempt, ReadyClaimVersion,
                ReadySequence, null, null, Root, FirstLeaseVersion, time.AddSeconds(LeaseSeconds)),
            _ => new(Item, MessageState.Acked, FirstAttempt, ReadyAckVersion, ReadySequence, null, null,
                LeaseVersion: FirstLeaseVersion)
        };
    }

    private static MessageMetadata ScheduledMetadata(EventQueueStateEnvelopeStage stage, EventQueueStateEnvelopeFixture fixture)
    {
        return stage switch
        {
            EventQueueStateEnvelopeStage.Completed => new(Scheduled, MessageState.Acked, FirstAttempt, ScheduledAckVersion,
                ScheduledReadySequence, null, null, LeaseVersion: FirstLeaseVersion),
            EventQueueStateEnvelopeStage.ScheduledClaimed => new(Scheduled, MessageState.Leased, FirstAttempt,
                ScheduledClaimVersion, ScheduledReadySequence, null, null, Root, FirstLeaseVersion,
                fixture.ScheduledAt.AddSeconds(LeaseSeconds)),
            _ => new(Scheduled, MessageState.Scheduled, NoUsage, InitialStateVersion, NoUsage, fixture.ScheduledAt, null)
        };
    }

    private static async Task<long> MessageAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state,
        MessageMetadata metadata)
    {
        var retained = metadata.State != MessageState.Acked;
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Database.InspectMessage(Root, fixture.Lane, metadata.Id),
            new MessageInspection(metadata, retained ? Payload : null, retained ? Headers : null));
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Store.Read(view => view.GetRecord<MessageMetadata>(
            KeySpace.Partition(MetadataFamily, fixture.Partition, Queue, metadata.Id))), metadata);
        var key = KeySpace.Partition(BodyFamily, fixture.Partition, Queue, metadata.Id);
        var body = fixture.Store.Read(view => view.ReadOwnedValue(key));
        if (!retained)
        {
            await Assert.That(body).IsNull();
            return NoUsage;
        }
        var input = EventQueueStateEnvelopeProducer.Message(fixture, metadata.Id == Scheduled);
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.AsSpan().SequenceEqual(state.Bodies[Convert.ToHexString(key)])).IsTrue();
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(OrleansSerialization.Deserialize<MessageBody>(body),
            new MessageBody(metadata.Id, Payload, Headers, null, JsonData.Fingerprint(input)));
        return body.LongLength;
    }

    private static int ExpectedCount(string family, EventQueueStateEnvelopeStage stage, int retained, int references)
    {
        if (family == MetadataFamily)
        {
            return TwoMessages;
        }
        if (family == BodyFamily)
        {
            return retained;
        }
        if (family == CountersFamily)
        {
            return SingleResult;
        }
        if (family != InboxFamily)
        {
            return references;
        }
        return stage >= EventQueueStateEnvelopeStage.Processed ? SingleResult : NoUsage;
    }

    private static async Task IndexesAsync(EventQueueStateEnvelopeFixture fixture, MessageMetadata ready,
        MessageMetadata scheduled, EventQueueStateEnvelopeStage stage, int retained)
    {
        foreach (var family in new[] { MetadataFamily, BodyFamily, CountersFamily, ReadyFamily, ScheduledFamily,
            LeaseFamily, InboxFamily, DeadLetterFamily, PendingFamily, ParkedFamily, OrderFamily })
        {
            var expected = new List<(byte[] Key, string Id)>();
            foreach (var metadata in new[] { ready, scheduled })
            {
                if (family == ReadyFamily && metadata.State == MessageState.Ready)
                {
                    expected.Add((KeySpace.Partition(family, fixture.Partition, Queue, metadata.ReadySequence, metadata.Id), metadata.Id));
                }
                if (family == ScheduledFamily && metadata.State == MessageState.Scheduled)
                {
                    expected.Add((KeySpace.Partition(family, fixture.Partition, Queue, fixture.ScheduledAt, metadata.Id), metadata.Id));
                }
                if (family == LeaseFamily && metadata.State == MessageState.Leased)
                {
                    expected.Add((KeySpace.Partition(family, fixture.Partition, Queue, metadata.LeaseUntil, metadata.Id), metadata.Id));
                }
            }
            var page = fixture.Store.Read(view => view.Scan(KeySpace.Partition(family, fixture.Partition, Queue), ScanBound));
            await Assert.That(page.HasMore).IsFalse();
            var count = ExpectedCount(family, stage, retained, expected.Count);
            await Assert.That(page.Records.Length).IsEqualTo(count);
            foreach (var entry in expected)
            {
                await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Store.Read(view => view.GetRecord<string>(entry.Key)), entry.Id);
            }
        }
    }
}
