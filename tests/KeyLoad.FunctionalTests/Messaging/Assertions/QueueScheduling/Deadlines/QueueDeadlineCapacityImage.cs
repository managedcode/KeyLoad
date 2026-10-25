using KeyLoad.Core.Messaging;
using static KeyLoad.FunctionalTests.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueDeadlineCapacityImage
{
    internal static async Task RequireAsync(DatabaseEngine database, QueueLaneRef lane,
        (EnqueueMessage Input, MessageMetadata Metadata)[] messages, long sequence, long parkedSequence = NoUsage)
    {
        Dictionary<string, List<string>> expected = new(StringComparer.Ordinal);
        foreach (var family in new[] { Metadata, Body, Counters, Ready, Scheduled, Lease, Pending, Parked, DeadLetter, Order, Inbox })
        {
            expected.Add(family, []);
        }
        long storedBytes = NoUsage;
        long leasedBytes = NoUsage;
        long parkedBytes = NoUsage;
        foreach (var (input, metadata) in messages)
        {
            Add(expected[Metadata], KeySpace.Partition(Metadata, lane.Partition, lane.Queue, metadata.Id), metadata);
            var retained = metadata.State is not (MessageState.Acked or MessageState.Expired);
            await Assert.That(database.InspectMessage(Root, lane, metadata.Id)).IsEqualTo(
                new MessageInspection(metadata, retained ? QueueDeadlineCapacityProtocol.Json : null, retained ? QueueDeadlineCapacityProtocol.Json : null));
            if (retained)
            {
                var body = new MessageBody(metadata.Id, QueueDeadlineCapacityProtocol.Json, new string(QueueDeadlineCapacityProtocol.Json.AsSpan()), input.OrderingKey, JsonData.Fingerprint(input));
                Add(expected[Body], KeySpace.Partition(Body, lane.Partition, lane.Queue, metadata.Id), body);
                var bytes = OrleansSerialization.Serialize(body).LongLength;
                storedBytes += bytes;
                leasedBytes += metadata.State == MessageState.Leased ? bytes : NoUsage;
                parkedBytes += metadata.State == MessageState.DeadLettered ? bytes : NoUsage;
            }
            AddReference(expected, lane, metadata);
            AddDeadLetter(expected, lane, metadata);
        }
        Add(expected[Counters], KeySpace.Partition(Counters, lane.Partition, lane.Queue), new QueueCounters(
            messages.Count(row => row.Metadata.State is not (MessageState.Acked or MessageState.Expired)), storedBytes,
            messages.Count(row => row.Metadata.State == MessageState.Leased), leasedBytes, sequence,
            messages.Count(row => row.Metadata.State == MessageState.DeadLettered), parkedBytes, parkedSequence));
        foreach (var (family, rows) in expected)
        {
            var page = database.Store.Read(view => view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), ImageBound));
            await Assert.That(page.HasMore).IsFalse();
            await Assert.That(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ByteSeparator + Convert.ToHexString(row.Value.Span)).Order())
                .IsEquivalentTo(rows.Order(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
    }

    private static void AddReference(Dictionary<string, List<string>> expected, QueueLaneRef lane, MessageMetadata metadata)
    {
        if (metadata.State == MessageState.Ready)
        {
            Add(expected[Ready], KeySpace.Partition(Ready, lane.Partition, lane.Queue, metadata.ReadySequence, metadata.Id), metadata.Id);
        }
        if (metadata.State == MessageState.Scheduled)
        {
            Add(expected[Scheduled], KeySpace.Partition(Scheduled, lane.Partition, lane.Queue, metadata.NotBefore!.Value, metadata.Id), metadata.Id);
        }
        if (metadata.State == MessageState.Leased)
        {
            Add(expected[Lease], KeySpace.Partition(Lease, lane.Partition, lane.Queue, metadata.LeaseUntil!.Value, metadata.Id), metadata.Id);
        }
    }

    private static void AddDeadLetter(Dictionary<string, List<string>> expected, QueueLaneRef lane, MessageMetadata metadata)
    {
        if (metadata.State == MessageState.PendingDeadLetter)
        {
            Add(expected[Pending], KeySpace.Partition(Pending, lane.Partition, lane.Queue, metadata.Id),
                new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, AttemptsExhausted));
        }
        if (metadata.State == MessageState.DeadLettered)
        {
            Add(expected[DeadLetter], KeySpace.Partition(DeadLetter, lane.Partition, lane.Queue, metadata.Id), metadata.Id);
            Add(expected[Parked], KeySpace.Partition(Parked, lane.Partition, lane.Queue, metadata.ParkedSequence, metadata.Id),
                new QueueDeadLetterReference(metadata.Id, metadata.ParkedSequence, metadata.DeliveryGeneration, AttemptsExhausted));
        }
    }

    private static void Add<T>(List<string> rows, byte[] key, T value)
        => rows.Add(Convert.ToHexString(key) + ByteSeparator + Convert.ToHexString(OrleansSerialization.Serialize(value)));
}
