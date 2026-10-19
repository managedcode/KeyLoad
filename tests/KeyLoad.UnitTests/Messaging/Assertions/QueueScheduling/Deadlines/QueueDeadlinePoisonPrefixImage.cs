using KeyLoad.Core;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlinePoisonPrefixImage
{
    internal static async Task RequireAsync(DatabaseEngine database, QueueLaneRef lane, MessageMetadata[] metadata,
        DateTimeOffset originalDue, long sequence)
    {
        var rows = new List<string>();
        long storedBytes = QueueDeadlinePoisonPrefixProtocol.Initial;
        long leasedBytes = QueueDeadlinePoisonPrefixProtocol.Initial;
        foreach (var value in metadata)
        {
            var retained = value.State is not (MessageState.Acked or MessageState.Expired);
            await Assert.That(database.InspectMessage(QueueDeadlinePoisonPrefixProtocol.Root, lane, value.Id)).IsEqualTo(
                new MessageInspection(value, retained ? QueueDeadlinePoisonPrefixProtocol.Json : null, retained ? QueueDeadlinePoisonPrefixProtocol.Json : null));
            Add(rows, KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Metadata, lane.Partition, lane.Queue, value.Id), value);
            if (retained)
            {
                var seed = new EnqueueMessage(lane.Queue, value.Id, QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json,
                    NotBefore: value.Id == QueueDeadlinePoisonPrefixProtocol.ScheduledMessage ? originalDue : null);
                var body = new MessageBody(value.Id, QueueDeadlinePoisonPrefixProtocol.Json, new string(QueueDeadlinePoisonPrefixProtocol.Json.AsSpan()), null, JsonData.Fingerprint(seed));
                Add(rows, KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Body, lane.Partition, lane.Queue, value.Id), body);
                var bytes = OrleansSerialization.Serialize(body).LongLength;
                storedBytes += bytes;
                if (value.State == MessageState.Leased)
                {
                    leasedBytes += bytes;
                }
            }
            if (value.State == MessageState.Ready)
            {
                Add(rows, KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Ready, lane.Partition, lane.Queue, value.ReadySequence, value.Id), value.Id);
            }
            if (value.State == MessageState.Leased)
            {
                Add(rows, KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Lease, lane.Partition, lane.Queue, value.LeaseUntil!.Value, value.Id), value.Id);
            }
        }
        Add(rows, KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Counters, lane.Partition, lane.Queue), new QueueCounters(
            metadata.Count(value => value.State is not (MessageState.Acked or MessageState.Expired)), storedBytes,
            metadata.Count(value => value.State == MessageState.Leased), leasedBytes, sequence));
        var actual = database.Store.Read(view =>
        {
            var families = new[] { QueueDeadlinePoisonPrefixProtocol.Metadata, QueueDeadlinePoisonPrefixProtocol.Body, QueueDeadlinePoisonPrefixProtocol.Ready, QueueDeadlinePoisonPrefixProtocol.Lease, QueueDeadlinePoisonPrefixProtocol.Scheduled, QueueDeadlinePoisonPrefixProtocol.Counters };
            return families.SelectMany(family =>
            {
                var page = view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), QueueDeadlinePoisonPrefixProtocol.ImageRecordBound);
                if (page.HasMore)
                {
                    throw new InvalidOperationException(QueueDeadlinePoisonPrefixProtocol.IncompleteImage);
                }
                return page.Records;
            })
                .Select(row => Convert.ToHexString(row.Key.Span) + QueueDeadlinePoisonPrefixProtocol.ByteSeparator + Convert.ToHexString(row.Value.Span)).Order().ToArray();
        });
        await Assert.That(actual).IsEquivalentTo(rows.Order().ToArray(), CollectionOrdering.Matching);
    }

    private static void Add<T>(List<string> rows, byte[] key, T value)
        => rows.Add(Convert.ToHexString(key) + QueueDeadlinePoisonPrefixProtocol.ByteSeparator + Convert.ToHexString(OrleansSerialization.Serialize(value)));
}
