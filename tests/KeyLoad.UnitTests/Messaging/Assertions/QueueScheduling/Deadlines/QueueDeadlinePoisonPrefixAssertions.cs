using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlinePoisonPrefixAssertions
{
    internal static async Task CursorAndCancellationAsync(DatabaseEngine database, KeyLoad.Storage.ZoneTree.ZoneTreeStore store,
        DateTimeOffset due, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var key = store.Read(view => view.Scan(KeyCodec.Encode(QueueDeadlinePoisonPrefixProtocol.Scheduled), QueueDeadlinePoisonPrefixProtocol.First).Records.Single().Key.ToArray());
        var expected = key.ToArray();
        var owned = ImmutableArray.CreateRange(key);
        var cursor = QueueDeadlineCursor.Match(store.Identity, null) with
        {
            Scheduled = new DuePrefixCursor(true, owned, owned)
        };
        var deferred = QueueDeadlineCursor.DeferNext(store.Identity, cursor);
        await Assert.That(deferred.Next).IsEqualTo(QueueDeadlineCursor.LeasedIndex);
        await Assert.That(deferred.Scheduled.UpperKey!.Value.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(deferred.Scheduled.LastKey!.Value.AsSpan().SequenceEqual(expected)).IsTrue();
        key[QueueDeadlinePoisonPrefixProtocol.Initial] ^= QueueDeadlinePoisonPrefixProtocol.PoisonByte;
        await Assert.That(key.AsSpan().SequenceEqual(expected)).IsFalse();
        await Assert.That(deferred.Scheduled.UpperKey!.Value.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(deferred.Scheduled.LastKey!.Value.AsSpan().SequenceEqual(expected)).IsTrue();
        await Assert.That(cursor.Scheduled.UpperKey!.Value.AsSpan().SequenceEqual(expected)).IsTrue();
        var original = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeSerialization.Serialize(store.Identity);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Task.FromResult(
            QueueDeadlineDiscovery.ReadPage(database, null, due, cancelled.Token)));
        await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, original, position, identity);
    }

    internal static async Task FinalAsync(DatabaseEngine database, QueueLaneRef lane, DateTimeOffset due, bool leased)
    {
        await Assert.That(database.InspectMessage(QueueDeadlinePoisonPrefixProtocol.Root, lane, QueueDeadlinePoisonPrefixProtocol.ScheduledMessage)).IsEqualTo(
            new MessageInspection(new(QueueDeadlinePoisonPrefixProtocol.ScheduledMessage, MessageState.Ready, QueueDeadlinePoisonPrefixProtocol.Initial, QueueDeadlinePoisonPrefixProtocol.Second, leased ? QueueDeadlinePoisonPrefixProtocol.FirstAdvancedReadySequence : QueueDeadlinePoisonPrefixProtocol.FinalReadySequence, null, null), QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json));
        await Assert.That(database.InspectMessage(QueueDeadlinePoisonPrefixProtocol.Root, lane, QueueDeadlinePoisonPrefixProtocol.LeasedMessage)).IsEqualTo(
            new MessageInspection(new(QueueDeadlinePoisonPrefixProtocol.LeasedMessage, MessageState.Ready, QueueDeadlinePoisonPrefixProtocol.First, QueueDeadlinePoisonPrefixProtocol.ExpiredLeaseStateVersion, leased ? QueueDeadlinePoisonPrefixProtocol.FinalReadySequence : QueueDeadlinePoisonPrefixProtocol.FirstAdvancedReadySequence, null, null,
                LeaseVersion: QueueDeadlinePoisonPrefixProtocol.First), QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json));
        await Assert.That(database.InspectMessage(QueueDeadlinePoisonPrefixProtocol.Root, lane, QueueDeadlinePoisonPrefixProtocol.ExpiringMessage)).IsEqualTo(
            new MessageInspection(new(QueueDeadlinePoisonPrefixProtocol.ExpiringMessage, MessageState.Expired, QueueDeadlinePoisonPrefixProtocol.Initial, QueueDeadlinePoisonPrefixProtocol.Second, QueueDeadlinePoisonPrefixProtocol.Second, null, due), null, null));
        var scheduled = new EnqueueMessage(lane.Queue, QueueDeadlinePoisonPrefixProtocol.ScheduledMessage, QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json, NotBefore: due);
        var originalLease = new EnqueueMessage(lane.Queue, QueueDeadlinePoisonPrefixProtocol.LeasedMessage, QueueDeadlinePoisonPrefixProtocol.Json, QueueDeadlinePoisonPrefixProtocol.Json);
        var scheduledBody = new MessageBody(scheduled.MessageId, QueueDeadlinePoisonPrefixProtocol.Json, new string(QueueDeadlinePoisonPrefixProtocol.Json.AsSpan()), null, JsonData.Fingerprint(scheduled));
        var leasedBody = new MessageBody(originalLease.MessageId, QueueDeadlinePoisonPrefixProtocol.Json, new string(QueueDeadlinePoisonPrefixProtocol.Json.AsSpan()), null, JsonData.Fingerprint(originalLease));
        await Assert.That(database.Store.Read(view => view.GetRecord<MessageBody>(
            KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Body, lane.Partition, lane.Queue, scheduled.MessageId)))).IsEqualTo(scheduledBody);
        await Assert.That(database.Store.Read(view => view.GetRecord<MessageBody>(
            KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Body, lane.Partition, lane.Queue, originalLease.MessageId)))).IsEqualTo(leasedBody);
        var bodyBytes = NativeSerialization.Serialize(scheduledBody).Length + NativeSerialization.Serialize(leasedBody).Length;
        await Assert.That(database.Store.Read(view => view.GetRecord<QueueCounters>(
            KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Counters, lane.Partition, lane.Queue))))
            .IsEqualTo(new QueueCounters(QueueDeadlinePoisonPrefixProtocol.Second, bodyBytes, QueueDeadlinePoisonPrefixProtocol.Initial, QueueDeadlinePoisonPrefixProtocol.Initial, QueueDeadlinePoisonPrefixProtocol.FinalReadySequence));
        foreach (var family in new[] { QueueDeadlinePoisonPrefixProtocol.Scheduled, QueueDeadlinePoisonPrefixProtocol.Lease })
        {
            await Assert.That(database.Store.Read(view => view.Scan(KeySpace.Partition(family, lane.Partition, lane.Queue), QueueDeadlinePoisonPrefixProtocol.First).Records))
                .IsEmpty();
        }
        foreach (var entry in new[] { (Id: QueueDeadlinePoisonPrefixProtocol.ScheduledMessage, Sequence: leased ? (long)QueueDeadlinePoisonPrefixProtocol.FirstAdvancedReadySequence : (long)QueueDeadlinePoisonPrefixProtocol.FinalReadySequence),
            (Id: QueueDeadlinePoisonPrefixProtocol.LeasedMessage, Sequence: leased ? (long)QueueDeadlinePoisonPrefixProtocol.FinalReadySequence : (long)QueueDeadlinePoisonPrefixProtocol.FirstAdvancedReadySequence) })
        {
            await Assert.That(database.Store.Read(view => view.GetRecord<string>(
                KeySpace.Partition(QueueDeadlinePoisonPrefixProtocol.Ready, lane.Partition, lane.Queue, entry.Sequence, entry.Id))))
                .IsEqualTo(entry.Id);
        }
    }

}
