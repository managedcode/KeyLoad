using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class EventAppendReceiptAssertions
{
    private const string PutKind = "putDocument";
    private const string AppendKind = "appendEvents";
    private const string EnqueueKind = "enqueue";
    private const string OutboxSpace = "outbox";

    internal static async Task VerifyAsync(IAtomicStore store, CommitReceipt receipt, long expectedPosition)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(EventAppendCrashContract.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(EventAppendCrashContract.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(expectedPosition);

        var expected = new[] { new MutationReceipt(PutKind, EventAppendCrashContract.Documents, EventAppendCrashContract.Producer, 1),
            new MutationReceipt(AppendKind, EventAppendCrashContract.Streams, EventAppendCrashContract.StreamId, 2),
            new MutationReceipt(EnqueueKind, EventAppendCrashContract.Queue, EventAppendCrashContract.Producer, 1) };
        await Assert.That(receipt.Mutations.Select(value => Convert.ToHexString(NativeSerialization.Serialize(value))))
            .IsEquivalentTo(expected.Select(value => Convert.ToHexString(NativeSerialization.Serialize(value))), CollectionOrdering.Matching);
    }

    internal static async Task VerifyOutboxAsync(IAtomicStore store, CommitReceipt receipt, long seedTail, DateTimeOffset time)
    {
        for (var index = 0; index < receipt.Mutations.Length; index++)
        {
            var entry = store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition(OutboxSpace,
                EventAppendCrashContract.Partition, seedTail + index + 1L)));
            await Assert.That(entry).IsNotNull();
            await Assert.That(entry!.Commit).IsEqualTo(receipt.Token);
            await Assert.That(entry.Sequence).IsEqualTo(seedTail + index + 1L);
            await Assert.That(entry.Ordinal).IsEqualTo(index);
            await Assert.That(entry.CommittedAt).IsEqualTo(time);
            await Assert.That(entry.Before).IsNull();
            await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(entry.Receipt)))
                .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(receipt.Mutations[index])));
            var producer = receipt.CommandId == EventAppendCrashContract.CommandId ? EventAppendCrashContract.Producer : EventAppendCrashContract.Healthy;
            var request = EventAppendCrashContract.ProducerCommand(receipt.CommandId, producer, producer == EventAppendCrashContract.Producer ? 1 : 2);
            await Assert.That(JsonSerializer.Serialize(entry.Mutation, JsonDefaults.Options))
                .IsEqualTo(JsonSerializer.Serialize(request.Mutations[index], JsonDefaults.Options));
            if (index == 0)
            {
                var image = new DocumentRecord(new(EventAppendCrashContract.Partition,
                    EventAppendCrashContract.Documents, producer), 1L, EventAppendCrashContract.DocumentJson, new(), time);
                await Assert.That(Convert.ToHexString(NativeSerialization.Serialize(entry.After)))
                    .IsEqualTo(Convert.ToHexString(NativeSerialization.Serialize(image)));
            }
            else
            {
                await Assert.That(entry.After).IsNull();
            }
        }
    }

    internal static async Task AssertOutboxPresenceAsync(IAtomicStore store, long seedTail, bool committed)
    {
        for (var index = 0; index < 3; index++)
        {
            var entry = store.Read(view => view.ReadOwnedValue(KeySpace.Partition(OutboxSpace,
                EventAppendCrashContract.Partition, seedTail + index + 1L)));
            await Assert.That(entry is not null).IsEqualTo(committed);
        }
    }

    internal static async Task VerifyHealthyAsync(IAtomicStore store, CommitReceipt receipt)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(EventAppendCrashContract.HealthyCommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(EventAppendCrashContract.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(store.Position);

        await Assert.That(receipt.Durability).IsEqualTo(store.Identity.Durability);
        var expected = new[] { new MutationReceipt(PutKind, EventAppendCrashContract.Documents, EventAppendCrashContract.Healthy, 1),
            new MutationReceipt(AppendKind, EventAppendCrashContract.Streams, EventAppendCrashContract.StreamId, 3),
            new MutationReceipt(EnqueueKind, EventAppendCrashContract.Queue, EventAppendCrashContract.Healthy, 1) };
        await Assert.That(receipt.Mutations.Select(value => Convert.ToHexString(NativeSerialization.Serialize(value))))
            .IsEquivalentTo(expected.Select(value => Convert.ToHexString(NativeSerialization.Serialize(value))), CollectionOrdering.Matching);
    }

    internal static async Task SameResultAsync(Result<object> expected, Result<object> actual)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
