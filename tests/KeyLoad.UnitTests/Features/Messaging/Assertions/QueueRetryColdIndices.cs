using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryColdIndices
{
    internal static Task ScheduledAsync(DatabaseEngine database, QueueRetryColdState state,
        DateTimeOffset? retry, bool expired = false)
    {
        List<string> expected = [];
        if (!expired)
        {
            expected.Add(Row(state.Lane, QueueRetryColdProtocol.Scheduled, QueueRetryColdProtocol.Expiry,
            state.Start.AddSeconds(QueueRetryColdProtocol.ScheduledSeconds)));
        }
        if (retry is { } due)
        { expected.Add(Row(state.Lane, QueueRetryColdProtocol.Scheduled, QueueRetryColdProtocol.Retry, due)); }
        else
        {
            expected.Add(Row(state.Lane, QueueRetryColdProtocol.DeadLetter, QueueRetryColdProtocol.Retry));
            expected.Add(ParkedRow(state.Lane));
        }
        return CompareAsync(database, state, expected);
    }

    internal static async Task LeasedAsync(DatabaseEngine database, QueueRetryColdState state, Delivery delivery)
    {
        await CountersAsync(database, state, QueueRetryColdProtocol.Retry, QueueRetryColdProtocol.Expiry,
            delivery.LeaseVersion);
        await CompareAsync(database, state,
            [Row(state.Lane, QueueRetryColdProtocol.Scheduled, QueueRetryColdProtocol.Expiry,
                state.Start.AddSeconds(QueueRetryColdProtocol.ScheduledSeconds)),
             Row(state.Lane, QueueRetryColdProtocol.Lease, QueueRetryColdProtocol.Retry, delivery.LeaseUntil)]);
    }

    internal static async Task HealthyLeasedAsync(DatabaseEngine database, QueueRetryColdState state, Delivery delivery)
    {
        await Assert.That(delivery.Attempt).IsEqualTo(QueueRetryColdProtocol.One);
        await Assert.That(delivery.LeaseVersion).IsEqualTo(QueueRetryColdProtocol.FirstSequence);
        await Assert.That(delivery.DeliveryGeneration).IsEqualTo(QueueRetryColdProtocol.FirstSequence);
        await Assert.That(delivery.LeaseUntil).IsEqualTo(state.FinalTime.AddSeconds(QueueRetryColdProtocol.LeaseSeconds));
        await Assert.That(delivery.Token).IsNotEmpty();
        var inspected = database.InspectMessage(QueueRetryColdProtocol.Root, state.Lane, QueueRetryColdProtocol.Healthy)!;
        await Assert.That(inspected.Metadata).IsEqualTo(new MessageMetadata(QueueRetryColdProtocol.Healthy,
            MessageState.Leased, QueueRetryColdProtocol.One, QueueRetryColdProtocol.FirstClaimVersion,
            QueueRetryColdProtocol.HealthySequence, null, null, QueueRetryColdProtocol.Root,
            QueueRetryColdProtocol.FirstSequence, delivery.LeaseUntil));
        await CountersAsync(database, state, QueueRetryColdProtocol.Healthy, QueueRetryColdProtocol.Retry,
            QueueRetryColdProtocol.HealthySequence);
        await CompareAsync(database, state,
            [Row(state.Lane, QueueRetryColdProtocol.DeadLetter, QueueRetryColdProtocol.Retry), ParkedRow(state.Lane),
             Row(state.Lane, QueueRetryColdProtocol.Lease, QueueRetryColdProtocol.Healthy, delivery.LeaseUntil)]);
    }

    private static async Task CountersAsync(DatabaseEngine database, QueueRetryColdState state,
        string leased, string other, long sequence)
    {
        var leasedBytes = database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(QueueRetryColdProtocol.Body,
            state.Lane.Partition, state.Lane.Queue, leased))!.LongLength);
        var otherBytes = database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(QueueRetryColdProtocol.Body,
            state.Lane.Partition, state.Lane.Queue, other))!.LongLength);
        var actual = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueRetryColdProtocol.Counters,
            state.Lane.Partition, state.Lane.Queue)));
        await Assert.That(actual).IsEqualTo(new QueueCounters(QueueRetryColdProtocol.StoredCapacity,
            leasedBytes + otherBytes, QueueRetryColdProtocol.One, leasedBytes, sequence,
            leased == QueueRetryColdProtocol.Healthy ? QueueRetryColdProtocol.One : QueueRetryColdProtocol.Zero,
            leased == QueueRetryColdProtocol.Healthy ? otherBytes : QueueRetryColdProtocol.Zero,
            leased == QueueRetryColdProtocol.Healthy ? QueueRetryColdProtocol.FirstSequence : QueueRetryColdProtocol.Zero));
    }

    private static async Task CompareAsync(DatabaseEngine database, QueueRetryColdState state, List<string> expected)
    {
        string[] families = [QueueRetryColdProtocol.Ready, QueueRetryColdProtocol.Scheduled,
            QueueRetryColdProtocol.Lease, QueueRetryColdProtocol.DeadLetter, QueueRetryColdProtocol.Pending,
            QueueRetryColdProtocol.Parked, QueueRetryColdProtocol.Inbox];
        var prefixes = families.Select(family => Convert.ToHexString(KeySpace.Partition(family, state.Lane.Partition,
            state.Lane.Queue))).ToArray();
        var actual = QueueWholeFlowStorage.Bytes((ZoneTreeStore)database.Store)
            .Where(row => prefixes.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(actual).IsEquivalentTo(expected.Order(StringComparer.Ordinal).ToArray(), CollectionOrdering.Matching);
    }

    private static string ParkedRow(QueueLaneRef lane)
    {
        var key = KeySpace.Partition(QueueRetryColdProtocol.Parked, lane.Partition, lane.Queue,
            QueueRetryColdProtocol.FirstSequence, QueueRetryColdProtocol.Retry);
        var value = new KeyLoad.Core.Features.Messaging.QueueDeadLetterReference(QueueRetryColdProtocol.Retry,
            QueueRetryColdProtocol.FirstSequence, QueueRetryColdProtocol.FirstSequence, QueueRetryColdProtocol.ExhaustedCode);
        return Convert.ToHexString(key) + ":" + Convert.ToHexString(NativeSerialization.Serialize(value));
    }

    private static string Row(QueueLaneRef lane, string family, string id, DateTimeOffset? time = null)
    {
        var key = time is { } instant ? KeySpace.Partition(family, lane.Partition, lane.Queue, instant, id)
            : KeySpace.Partition(family, lane.Partition, lane.Queue, id);
        return Convert.ToHexString(key) + ":" + Convert.ToHexString(NativeSerialization.Serialize(id));
    }
}
