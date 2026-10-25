using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Messaging;

internal static class QueueExpiredReadyVersionExhaustionAssertions
{
    internal static MessageMetadata Expired(MessageMetadata original) => original with
    {
        State = MessageState.Expired,
        StateVersion = 2
    };

    internal static async Task<CommitToken> NextTokenAsync(DatabaseEngine database, QueueLaneRef lane)
    {
        var placement = database.ReadAtomicPartitionPlacement(QueueClaimVersionExhaustionProtocol.Root, lane.Partition);
        await Assert.That(placement.Partition).IsEqualTo(lane.Partition);
        return new(placement.Incarnation, lane.Partition.AtomicPartitionId, checked(database.LastApplied + 1));
    }

    internal static async Task EmptyAsync(Result<object> result, Guid id, CommitToken expected)
        => await QueueClaimVersionExhaustionAssertions.EqualAsync(result.Get<ReceiveResult>(), new ReceiveResult(id, [], expected));

    internal static async Task ReceiptAsync(Result<object> result, Guid id, CommitToken token,
        string kind, QueueLaneRef lane, string message, long revision)
    {
        var expected = new CommitReceipt(id, token, [new(kind, lane.Queue, message, revision)], DurabilityProfile.ProcessDurable, 1);
        await QueueClaimVersionExhaustionAssertions.EqualAsync(result.Get<CommitReceipt>(), expected);
    }

    internal static async Task CurrentAsync(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef lane,
        MessageMetadata metadata, MessageBody? body, long nextReadySequence)
    {
        await Assert.That(database.InspectMessage(QueueClaimVersionExhaustionProtocol.Root, lane, metadata.Id))
            .IsEqualTo(new MessageInspection(metadata, body?.PayloadJson, body?.HeadersJson));
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.BodySpace,
            lane.Partition, lane.Queue, metadata.Id)))).IsEqualTo(body);
        var retained = body is not null;
        var leased = metadata.State == MessageState.Leased;
        var bytes = retained ? OrleansSerialization.Serialize(body).LongLength : 0;
        await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.CounterSpace,
            lane.Partition, lane.Queue)))).IsEqualTo(new QueueCounters(retained ? 1 : 0, bytes, leased ? 1 : 0, leased ? bytes : 0, nextReadySequence));
        var ready = metadata.State == MessageState.Ready
            ? new[] { Convert.ToHexString(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.ReadySpace, lane.Partition, lane.Queue,
                metadata.ReadySequence, metadata.Id)) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(metadata.Id)) }
            : [];
        var lease = leased
            ? new[] { Convert.ToHexString(KeySpace.Partition(QueueClaimVersionExhaustionProtocol.LeaseSpace, lane.Partition, lane.Queue,
                metadata.LeaseUntil!.Value, metadata.Id)) + ":" + Convert.ToHexString(OrleansSerialization.Serialize(metadata.Id)) }
            : [];
        await Assert.That(References(store, lane, QueueClaimVersionExhaustionProtocol.ReadySpace)).IsEquivalentTo(ready, CollectionOrdering.Matching);
        await Assert.That(References(store, lane, QueueClaimVersionExhaustionProtocol.LeaseSpace)).IsEquivalentTo(lease, CollectionOrdering.Matching);
        await Assert.That(References(store, lane, QueueClaimVersionExhaustionProtocol.ScheduledSpace)).IsEmpty();
    }

    private static string[] References(ZoneTreeStore store, QueueLaneRef lane, string family) => store.Read(view =>
    {
        var page = view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), QueueClaimVersionExhaustionProtocol.ImageRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The expired-ready references exceeded the original fixture bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });
}
