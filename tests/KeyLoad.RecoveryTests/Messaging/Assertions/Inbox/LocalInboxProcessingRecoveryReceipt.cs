using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingRecoveryReceipt
{
    internal static async Task RequireAsync(ZoneTreeStore store, Command<DatabaseCommand> operation,
        CommitReceipt receipt, CommitToken expectedToken, long seedTail, EntityRefLifetimeV1 lifetime)
    {
        ImmutableArray<MutationReceipt> expected = [new("putDocument", LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Effect, 1L),
            new("appendEvents", LocalInboxProcessingCrashContract.Events, LocalInboxProcessingCrashContract.Effect, 1L),
            new("enqueue", LocalInboxProcessingCrashContract.Output, LocalInboxProcessingCrashContract.Message, 1L),
            new("Ack", LocalInboxProcessingCrashContract.Input, LocalInboxProcessingCrashContract.Message, LocalInboxProcessingCrashContract.FinalVersion)];
        await LocalInboxProcessingRecoveryImage.ExactAsync(receipt, new CommitReceipt(operation.CommandId, expectedToken, expected, store.Identity.Durability, (store.Identity.Durability == DurabilityProfile.Buffered ? 0 : 1)));
        var inbox = store.Read(view => view.GetRecord<InboxRecord>(LocalInboxProcessingRecoveryImage.InboxKey()))!;
        await Assert.That(string.IsNullOrEmpty(inbox.Fingerprint)).IsFalse();
        await LocalInboxProcessingRecoveryImage.ExactAsync(inbox.Receipt, receipt);
        var request = NativeSerialization.Deserialize<ProcessingRequest>(operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements);
        for (var ordinal = 0; ordinal < request.Effects.Length; ordinal++)
        {
            var sequence = seedTail + ordinal + 1L;
            var entry = store.Read(view => view.GetRecord<OutboxEntry>(KeySpace.Partition("outbox", request.Lane.Partition, sequence)))!;
            var after = ordinal == 0 ? new DocumentRecord(new(request.Lane.Partition,
                LocalInboxProcessingCrashContract.Documents, LocalInboxProcessingCrashContract.Effect)
            { Lifetime = lifetime }, 1L,
                LocalInboxProcessingCrashContract.Payload, new(), new DateTimeOffset(operation.Timestamp)) : null;
            var expectedEntry = new OutboxEntry(sequence, ordinal, expectedToken, new DateTimeOffset(operation.Timestamp),
                request.Effects[ordinal], expected[ordinal], null, after);
            await Assert.That(entry).IsNotNull();
            await Assert.That(entry.GetType()).IsEqualTo(typeof(OutboxEntry));
            await Assert.That(entry.Mutation.GetType()).IsEqualTo(request.Effects[ordinal].GetType());
            await LocalInboxProcessingRecoveryImage.ExactAsync(entry, expectedEntry);
        }
    }
}
