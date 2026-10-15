using System.Collections.Immutable;
using System.Text;
using KeyLoad.Core;
using KeyLoad.CrashHost.Features.Messaging;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryReceipt
{
    internal static async Task RequireAsync(ZoneTreeStore store, ReplicatedOperation operation,
        CommitReceipt receipt, long position, long seedTail)
    {
        ImmutableArray<MutationReceipt> expected = [new("putDocument", LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Effect, 1L),
            new("appendEvents", LocalInboxProcessingCrashContract.Events, LocalInboxProcessingCrashContract.Effect, 1L),
            new("enqueue", LocalInboxProcessingCrashContract.Output, LocalInboxProcessingCrashContract.Message, 1L),
            new("Ack", LocalInboxProcessingCrashContract.Input, LocalInboxProcessingCrashContract.Message, LocalInboxProcessingCrashContract.FinalVersion)];
        await LocalInboxProcessingRecoveryImage.ExactAsync(receipt, new CommitReceipt(operation.Id,
            new(store.Identity.Incarnation, LocalInboxProcessingCrashContract.Partition.AtomicPartitionId, position, 1L), expected,
            store.Identity.Durability));
        var inbox = store.Read(view => view.GetRecord<InboxRecord>(LocalInboxProcessingRecoveryImage.InboxKey()))!;
        await Assert.That(string.IsNullOrEmpty(inbox.Fingerprint)).IsFalse();
        await Assert.That(NativeSerialization.Serialize(inbox.Receipt)).IsEquivalentTo(NativeSerialization.Serialize(receipt), CollectionOrdering.Matching);
        var request = JsonDefaults.Deserialize<ProcessingRequest>(Encoding.UTF8.GetBytes(operation.PayloadJson));
        for (var ordinal = 0; ordinal < request.Effects.Length; ordinal++)
        {
            var sequence = seedTail + ordinal + 1L;
            var entry = store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition("outbox", request.Lane.Partition, sequence)))!;
            var after = ordinal == 0 ? new DocumentRecord(new(request.Lane.Partition,
                LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Effect), 1L,
                LocalInboxProcessingCrashContract.Payload, new(), operation.EvaluatedAt) : null;
            var expectedEntry = new OutboxEntry(sequence, ordinal, receipt.Token, operation.EvaluatedAt,
                request.Effects[ordinal], expected[ordinal], null, after);
            await Assert.That(NativeSerialization.Serialize(entry)).IsEquivalentTo(NativeSerialization.Serialize(expectedEntry), CollectionOrdering.Matching);
        }
    }
}
