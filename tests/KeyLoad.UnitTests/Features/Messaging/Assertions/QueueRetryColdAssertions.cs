using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueRetryColdAssertions
{
    internal static string[] LaneBytes(ZoneTreeStore store, QueueLaneRef lane)
    {
        string[] families = [QueueRetryColdProtocol.Body, QueueRetryColdProtocol.Metadata, QueueRetryColdProtocol.Counters,
            QueueRetryColdProtocol.Ready, QueueRetryColdProtocol.Scheduled, QueueRetryColdProtocol.Lease,
            QueueRetryColdProtocol.DeadLetter, QueueRetryColdProtocol.Pending, QueueRetryColdProtocol.Parked, QueueRetryColdProtocol.Inbox];
        var prefixes = families.Select(family => Convert.ToHexString(KeySpace.Partition(family, lane.Partition, lane.Queue))).ToArray();
        return QueueWholeFlowStorage.Bytes(store).Where(row => prefixes.Any(prefix => row.StartsWith(prefix, StringComparison.Ordinal))).ToArray();
    }

    internal static async Task DeliveryAsync(DatabaseEngine database, Delivery actual, QueueRetryColdState state,
        int attempt, long sequence, long version, DateTimeOffset time)
    {
        await Assert.That(actual.Id).IsEqualTo(QueueRetryColdProtocol.Retry);
        await Assert.That(actual.PayloadJson).IsEqualTo(QueueRetryColdProtocol.CallerPayload);
        await Assert.That(actual.HeadersJson).IsEqualTo(QueueRetryColdProtocol.Headers);
        await Assert.That(actual.Attempt).IsEqualTo(attempt);
        await Assert.That(actual.LeaseVersion).IsEqualTo((long)attempt);
        await Assert.That(actual.DeliveryGeneration).IsEqualTo(QueueRetryColdProtocol.FirstSequence);
        await Assert.That(actual.LeaseUntil).IsEqualTo(time.AddSeconds(QueueRetryColdProtocol.LeaseSeconds));
        await Assert.That(database.Verify<DeliveryClaims>(actual.Token)).IsEqualTo(new DeliveryClaims(
            state.Lane, QueueRetryColdProtocol.Retry, QueueRetryColdProtocol.Root, (long)attempt,
            QueueRetryColdProtocol.FirstSequence, database.Store.Identity.Incarnation));
        await PendingAsync(database, state, new(QueueRetryColdProtocol.Retry, MessageState.Leased, attempt, version,
            sequence, null, null, QueueRetryColdProtocol.Root, actual.LeaseVersion, actual.LeaseUntil,
            SafeFailureCode: attempt == QueueRetryColdProtocol.One ? null : QueueRetryColdProtocol.RetryCode));
        await QueueRetryColdIndices.LeasedAsync(database, state, actual);
    }

    internal static async Task DeliveryReceiptAsync(CommitReceipt actual, DeliveryCommand command, long version)
    {
        await Assert.That(actual.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(actual.Token.AtomicPartitionId).IsEqualTo(command.Lane.Partition.AtomicPartitionId);
        MutationReceipt[] expected = [new(command.Action.ToString(), command.Lane.Queue,
            command.Action == DeliveryAction.Ack ? QueueRetryColdProtocol.Healthy : QueueRetryColdProtocol.Retry, version)];
        await Assert.That(JsonDefaults.Serialize(actual.Mutations).SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
        foreach (var mutation in actual.Mutations)
        { await Assert.That(mutation.CompositionReferences).IsEmpty(); }
    }

    internal static async Task PendingAsync(DatabaseEngine database, QueueRetryColdState state, MessageMetadata expected)
    {
        var actual = database.InspectMessage(QueueRetryColdProtocol.Root, state.Lane, QueueRetryColdProtocol.Retry)!;
        await Assert.That(actual.Metadata).IsEqualTo(expected);
        await Assert.That(actual.PayloadJson).IsEqualTo(QueueRetryColdProtocol.CallerPayload);
        await Assert.That(actual.HeadersJson).IsEqualTo(QueueRetryColdProtocol.Headers);
        var body = database.Store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(QueueRetryColdProtocol.Body,
            state.Lane.Partition, state.Lane.Queue, QueueRetryColdProtocol.Retry)));
        var expectedMutation = new EnqueueMessage(state.Lane.Queue, QueueRetryColdProtocol.Retry,
            QueueRetryColdProtocol.Payload, QueueRetryColdProtocol.Headers, OrderingKey: QueueRetryColdProtocol.OrderingKey);
        await Assert.That(body).IsEqualTo(new MessageBody(QueueRetryColdProtocol.Retry, QueueRetryColdProtocol.CallerPayload,
            QueueRetryColdProtocol.Headers, QueueRetryColdProtocol.OrderingKey, JsonData.Fingerprint(expectedMutation)));
    }

    internal static async Task TerminalAsync(DatabaseEngine database, QueueRetryColdState state, bool healthy)
    {
        await PendingAsync(database, state, new(QueueRetryColdProtocol.Retry, MessageState.DeadLettered,
            QueueRetryColdProtocol.MaximumAttempts, QueueRetryColdProtocol.TerminalVersion, QueueRetryColdProtocol.LastSequence,
            null, null, LeaseVersion: QueueRetryColdProtocol.LastSequence, SafeFailureCode: QueueRetryColdProtocol.ExhaustedCode, ParkedSequence: QueueRetryColdProtocol.FirstSequence));
        var marker = database.Store.Read(view => view.GetRecord<string>(KeySpace.Partition(QueueRetryColdProtocol.DeadLetter,
            state.Lane.Partition, state.Lane.Queue, QueueRetryColdProtocol.Retry)));
        await Assert.That(marker).IsEqualTo(QueueRetryColdProtocol.Retry);
        var bytes = database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(QueueRetryColdProtocol.Body,
            state.Lane.Partition, state.Lane.Queue, QueueRetryColdProtocol.Retry))!.LongLength);
        var parkedBytes = bytes;
        if (!healthy)
        {
            bytes += database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(QueueRetryColdProtocol.Body,
                state.Lane.Partition, state.Lane.Queue, QueueRetryColdProtocol.Expiry))!.LongLength);
        }
        var counters = database.Store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(QueueRetryColdProtocol.Counters,
            state.Lane.Partition, state.Lane.Queue)));
        await Assert.That(counters).IsEqualTo(new QueueCounters(healthy ? QueueRetryColdProtocol.One : QueueRetryColdProtocol.StoredCapacity,
            bytes, QueueRetryColdProtocol.Zero, QueueRetryColdProtocol.Zero, healthy ? QueueRetryColdProtocol.HealthySequence : QueueRetryColdProtocol.LastSequence,
            QueueRetryColdProtocol.One, parkedBytes, QueueRetryColdProtocol.FirstSequence));
        await QueueRetryColdIndices.ScheduledAsync(database, state, null, healthy);
        if (healthy)
        {
            await ExpiredAsync(database, state);
            var acknowledged = database.InspectMessage(QueueRetryColdProtocol.Root, state.Lane, QueueRetryColdProtocol.Healthy)!;
            await Assert.That(acknowledged.Metadata).IsEqualTo(new MessageMetadata(QueueRetryColdProtocol.Healthy,
                MessageState.Acked, QueueRetryColdProtocol.One, QueueRetryColdProtocol.FirstRetryVersion,
                QueueRetryColdProtocol.HealthySequence, null, null, LeaseVersion: QueueRetryColdProtocol.FirstSequence));
            await Assert.That(acknowledged.PayloadJson).IsNull();
            await Assert.That(acknowledged.HeadersJson).IsNull();
        }
    }

    internal static async Task ExpiredAsync(DatabaseEngine database, QueueRetryColdState state)
    {
        var expired = database.InspectMessage(QueueRetryColdProtocol.Root, state.Lane, QueueRetryColdProtocol.Expiry)!;
        await Assert.That(expired.Metadata).IsEqualTo(new MessageMetadata(QueueRetryColdProtocol.Expiry, MessageState.Expired,
            QueueRetryColdProtocol.Zero, QueueRetryColdProtocol.FirstClaimVersion, QueueRetryColdProtocol.Zero,
            state.Start.AddSeconds(QueueRetryColdProtocol.ScheduledSeconds), state.Start.AddSeconds(QueueRetryColdProtocol.ExpirySeconds)));
        await Assert.That(expired.PayloadJson).IsNull();
        await Assert.That(expired.HeadersJson).IsNull();
    }
}
