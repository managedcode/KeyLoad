using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueExpiredDlqAssertions
{
    private const string ExpiredDetail = "The original queue message expiry prevents this transition.";
    private const string Exhausted = "AttemptsExhausted";

    internal static byte[] ReadBody(IAtomicStore store, QueueLifecycleTestState state, string id)
        => store.Read(view => view.ReadOwnedValue(KeySpace.Partition(
            QueueLifecycleTestProtocol.BodySpace, state.Partition, state.Lane.Queue, id))
            ?? throw new InvalidOperationException("The genuine retained DLQ body is missing."));

    internal static async Task RetainedAsync(DatabaseEngine database, QueueExpiredDlqState state, bool parkedCancelled)
    {
        var operations = state.Operations;
        await MetadataAsync(database, operations, QueueLifecycleTestProtocol.Parked,
            new(QueueLifecycleTestProtocol.Parked, parkedCancelled ? MessageState.Cancelled : MessageState.DeadLettered,
                1, parkedCancelled ? 4L : 3L, 1L, null, state.Expiry, LeaseVersion: parkedCancelled ? 2L : 1L,
                DeliveryGeneration: parkedCancelled ? 2L : 1L, SafeFailureCode: Exhausted,
                ParkedSequence: parkedCancelled ? 0L : 1L));
        await MetadataAsync(database, operations, QueueLifecycleTestProtocol.Pending,
            new(QueueLifecycleTestProtocol.Pending, MessageState.PendingDeadLetter, 1, 3L, 2L, null, state.Expiry,
                LeaseVersion: 1L, SafeFailureCode: Exhausted));
        if (!parkedCancelled)
        {
            await BodyAsync(database.Store, operations, QueueLifecycleTestProtocol.Parked, state.Expiry, state.ParkedBody);
        }
        await BodyAsync(database.Store, operations, QueueLifecycleTestProtocol.Pending, state.Expiry, state.PendingBody);
        await CountersAsync(database.Store, operations, parkedCancelled
            ? new(1L, state.PendingBody.LongLength, 0L, 0L, 2L, 0L, 0L, 1L)
            : new(2L, state.ParkedBody.LongLength + state.PendingBody.LongLength, 0L, 0L, 2L,
                1L, state.ParkedBody.LongLength, 1L));
    }

    internal static async Task BodyAsync(IAtomicStore store, QueueLifecycleTestState state, string id,
        DateTimeOffset expiry, byte[] original)
    {
        await Assert.That(ReadBody(store, state, id)).IsEquivalentTo(original, CollectionOrdering.Matching);
        var literal = QueueLifecycleHealthy.Enqueue(id) with
        {
            ExpiresAt = expiry
        };
        var expected = new MessageBody(id, literal.PayloadJson, literal.HeadersJson, literal.OrderingKey, JsonData.Fingerprint(literal));
        await Assert.That(store.Read(view => view.GetRecord<MessageBody>(KeySpace.Partition(
            QueueLifecycleTestProtocol.BodySpace, state.Partition, state.Lane.Queue, id)))).IsEqualTo(expected);
    }

    internal static async Task MetadataAsync(DatabaseEngine database, QueueLifecycleTestState state, string id, MessageMetadata expected)
    {
        var actual = database.InspectMessage(QueueLifecycleTestProtocol.Administrator, state.Lane, id)!;
        await Assert.That(JsonSerializer.Serialize(actual.Metadata, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));
        var terminal = expected.State is MessageState.Cancelled or MessageState.Acked;
        await Assert.That(actual.PayloadJson).IsEqualTo(terminal ? null : QueueLifecycleTestProtocol.Payload);
        await Assert.That(actual.HeadersJson).IsEqualTo(terminal ? null : QueueLifecycleTestProtocol.Headers);
        if (terminal)
        {
            await Assert.That(database.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(
                QueueLifecycleTestProtocol.BodySpace, state.Partition, state.Lane.Queue, id)))).IsNull();
        }
    }

    internal static async Task RefusalAsync(DatabaseEngine database, global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, QueueLifecycleTestState state,
        Mutation mutation, ErrorCode code)
    {
        var image = QueueLifecycleImage.Capture(store, state.Lane);
        var actual = state.Batch(database, mutation);
        await Assert.That(actual.Error).IsEqualTo(code);
        if (code == ErrorCode.Validation)
        {
            await Assert.That(actual.SafeDetail).IsEqualTo(ExpiredDetail);
        }
        await Assert.That(actual.Value).IsNull();
        await QueueLifecycleImage.SameAsync(store, state.Lane, image);
    }

    internal static async Task CountersAsync(IAtomicStore store, QueueLifecycleTestState state, QueueCounters expected)
        => await Assert.That(store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(
            QueueLifecycleTestProtocol.CounterSpace, state.Partition, state.Lane.Queue)))).IsEqualTo(expected);

    internal static async Task TerminalAsync(DatabaseEngine database, QueueExpiredDlqState state, bool healthy)
    {
        foreach (var (id, sequence) in new[] { (QueueLifecycleTestProtocol.Parked, 1L), (QueueLifecycleTestProtocol.Pending, 2L) })
        {
            await MetadataAsync(database, state.Operations, id, new(id, MessageState.Cancelled, 1, 4L,
                sequence, null, state.Expiry, LeaseVersion: 2L, DeliveryGeneration: 2L, SafeFailureCode: Exhausted));
        }
        await CountersAsync(database.Store, state.Operations, new(0L, 0L, 0L, 0L, healthy ? 3L : 2L, 0L, 0L, 1L));
        foreach (var family in new[] { QueueLifecycleTestProtocol.ReadySpace, QueueLifecycleTestProtocol.ScheduledSpace,
            QueueLifecycleTestProtocol.LeaseSpace, QueueLifecycleTestProtocol.BodySpace, QueueLifecycleTestProtocol.DeadLetterSpace,
            QueueLifecycleTestProtocol.PendingSpace, QueueLifecycleTestProtocol.ParkedSpace })
        {
            var page = database.Store.Read(view => view.Scan(KeySpace.Partition(
                family, state.Operations.Partition, state.Operations.Lane.Queue), QueueLifecycleTestProtocol.One));
            await Assert.That(page.Records).IsEmpty();
            await Assert.That(page.HasMore).IsFalse();
        }
    }

    internal static async Task CancelAsync(DatabaseEngine database, QueueLifecycleTestState state, string id)
    {
        var original = state.Batch(database, new CancelQueueMessage(state.Lane.Queue, id, 3L, 1L)).Get<CommitReceipt>();
        await Assert.That(original.CommandId).IsEqualTo(state.Successes.Last().Operation.CommandId);
        await Assert.That(original.Mutations).IsEquivalentTo(
            new[] { new MutationReceipt("cancelQueueMessage", state.Lane.Queue, id, 4L) }, CollectionOrdering.Matching);
    }
}
