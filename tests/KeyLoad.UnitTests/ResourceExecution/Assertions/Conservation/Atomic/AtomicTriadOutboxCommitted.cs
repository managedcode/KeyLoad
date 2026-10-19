using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class AtomicTriadOutboxCommitted
{
    private const long FirstSequence = 2L;
    private const long FinalSequence = 4L;

    internal static async Task RequireAsync(DatabaseEngine engine, ZoneTreeStore store, CommandRequest command,
        CommitReceipt receipt)
    {
        var partition = command.Partition;
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsEqualTo(store.Position);

        await Assert.That(receipt.Durability).IsEqualTo(engine.Durability);
        var expectedReceipts = AtomicTriadOutboxFixture.Receipts(command);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(expectedReceipts.Length);
        for (var ordinal = 0; ordinal < expectedReceipts.Length; ordinal++)
        {
            await ReceiptAsync(receipt.Mutations[ordinal], expectedReceipts[ordinal]);
        }
        var document = engine.GetDocument(AtomicTriadOutboxFixture.Root,
            new(partition, AtomicTriadOutboxFixture.Collection, AtomicTriadOutboxFixture.Entity))!;
        await Assert.That(document.Json).IsEqualTo(AtomicTriadOutboxFixture.Payload);
        await Assert.That(document.Revision).IsEqualTo(AtomicTriadOutboxFixture.InitialRevision);
        await Assert.That(document.Reference).IsEqualTo(new EntityRef(partition,
            AtomicTriadOutboxFixture.Collection, AtomicTriadOutboxFixture.Entity));
        await Assert.That(document.Redacted).IsFalse();
        await Assert.That(document.RedactedFields).IsEmpty();
        var streamRef = new StreamRef(partition, AtomicTriadOutboxFixture.Streams, AtomicTriadOutboxFixture.Entity);
        var stream = engine.ReadStream(AtomicTriadOutboxFixture.Root, streamRef);
        await AtomicTriadOutboxImage.ExactAsync(stream.Head, new StreamHead(1L, 1L, 1L));
        var item = await Assert.That(stream.Events).HasSingleItem();
        await Assert.That(item.Stream).IsEqualTo(streamRef);
        await Assert.That(item.Revision).IsEqualTo(1L);
        await Assert.That(item.EventSequence).IsEqualTo(1L);
        await AtomicTriadOutboxImage.ExactAsync(item.Data,
            new EventData(AtomicTriadOutboxFixture.Entity, "Created", AtomicTriadOutboxFixture.Payload, AtomicTriadOutboxFixture.Headers));
        var message = engine.InspectMessage(AtomicTriadOutboxFixture.Root,
            new(partition, AtomicTriadOutboxFixture.Queue), AtomicTriadOutboxFixture.Entity)!;
        await AtomicTriadOutboxImage.ExactAsync(message.Metadata,
            new MessageMetadata(AtomicTriadOutboxFixture.Entity, MessageState.Ready, 0, 1L, 1L, null, null));
        await Assert.That(message.PayloadJson).IsEqualTo(AtomicTriadOutboxFixture.Payload);
        await Assert.That(message.HeadersJson).IsEqualTo(AtomicTriadOutboxFixture.Headers);
        await OutboxAsync(engine, store, command, receipt, item.RecordedAt);
    }

    private static async Task OutboxAsync(DatabaseEngine engine, ZoneTreeStore store,
        CommandRequest command, CommitReceipt receipt, DateTimeOffset recordedAt)
    {
        long bytes = 0;
        for (var ordinal = 0; ordinal < command.Mutations.Length; ordinal++)
        {
            var sequence = FirstSequence + ordinal;
            var raw = store.Read(view => view.ReadOwnedValue(KeySpace.Partition("outbox", command.Partition, sequence)))!;
            bytes += raw.LongLength;
            var entry = OrleansSerialization.Deserialize<OutboxEntry>(raw);
            await Assert.That(entry.Sequence).IsEqualTo(sequence);
            await Assert.That(entry.Ordinal).IsEqualTo(ordinal);
            await Assert.That(entry.Commit).IsEqualTo(receipt.Token);
            await Assert.That(entry.CommittedAt).IsEqualTo(recordedAt);
            await Assert.That(entry.Before).IsNull();
            var after = command.Mutations[ordinal] is PutDocument
                ? new DocumentRecord(new(command.Partition, AtomicTriadOutboxFixture.Collection, AtomicTriadOutboxFixture.Entity),
                    AtomicTriadOutboxFixture.InitialRevision, AtomicTriadOutboxFixture.Payload, new(), recordedAt) : null;
            await AtomicTriadOutboxImage.ExactAsync(entry.After, after);
            await Assert.That(JsonDefaults.Serialize<Mutation>(entry.Mutation).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize<Mutation>(command.Mutations[ordinal]))).IsTrue();
            await ReceiptAsync(entry.Receipt, AtomicTriadOutboxFixture.Receipts(command)[ordinal]);
        }
        await AtomicTriadOutboxImage.ExactAsync(engine.GetOutboxStatus(AtomicTriadOutboxFixture.Root, command.Partition).Head,
            new OutboxHead(FinalSequence, FirstSequence, AtomicTriadOutboxFixture.Capacity, bytes));
    }

    private static async Task ReceiptAsync(MutationReceipt actual, MutationReceipt expected)
    {
        await Assert.That(actual.Kind).IsEqualTo(expected.Kind);
        await Assert.That(actual.Resource).IsEqualTo(expected.Resource);
        await Assert.That(actual.Id).IsEqualTo(expected.Id);
        await Assert.That(actual.Revision).IsEqualTo(expected.Revision);
        await Assert.That(actual.CompositionReferences).IsEmpty();
        await Assert.That(expected.CompositionReferences).IsEmpty();
    }

}
