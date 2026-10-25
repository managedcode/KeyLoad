namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class AtomicTriadStagedOutbox
{
    private const string OutboxFamily = "outbox";
    private const string DocumentEpochFamily = "document-epoch";

    internal static async Task ReceiptAsync(CommitReceipt actual, CommitReceipt expected)
    {
        await AtomicTriadStagedCurrent.EqualAsync(actual, expected);
        await Assert.That(actual.Mutations.Length).IsEqualTo(AtomicTriadStagedProtocol.MutationCount);
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }

    internal static async Task CompleteAsync(AtomicTriadStagedFixture state)
    {
        var first = new DocumentRecord(state.Harness.ExpectedReference(new(state.Partition, AtomicTriadStagedProtocol.Collection, AtomicTriadStagedProtocol.Entity)),
            1L, AtomicTriadStagedProtocol.FirstJson, new(), state.CommittedAt);
        var second = first with
        {
            Revision = 2L,
            Json = AtomicTriadStagedProtocol.SecondJson
        };
        var bytes = 0L;
        for (var ordinal = 0; ordinal < AtomicTriadStagedProtocol.MutationCount; ordinal++)
        {
            var sequence = (long)ordinal + AtomicTriadStagedProtocol.InitialRevision;
            var raw = state.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(OutboxFamily, state.Partition, sequence)))
                ?? throw new InvalidOperationException(AtomicTriadStagedProtocol.Missing);
            bytes += raw.LongLength;
            var entry = OrleansSerialization.Deserialize<OutboxEntry>(raw);
            var before = ordinal == AtomicTriadStagedProtocol.ReplacementOrdinal ? first : null;
            var after = ordinal == 0 ? first : ordinal == AtomicTriadStagedProtocol.ReplacementOrdinal ? second : null;
            await AtomicTriadStagedCurrent.EqualAsync(entry, new OutboxEntry(sequence, ordinal, state.Receipt.Token,
                state.CommittedAt, state.Healthy.Mutations[ordinal], AtomicTriadStagedRecipe.Receipts[ordinal], before, after));
            await Assert.That(entry.Receipt.CompositionReferences).IsEmpty();
        }
        await AtomicTriadStagedCurrent.EqualAsync(state.Owner.GetOutboxStatus(AtomicTriadStagedProtocol.Root, state.Partition),
            new OutboxStatus(new(6L, 1L, 6L, bytes), []));
        await Assert.That(state.Store.Read(view => OrleansSerialization.Deserialize<long>(view.ReadOwnedValue(
            KeySpace.Partition(DocumentEpochFamily, state.Partition, AtomicTriadStagedProtocol.Collection))!))).IsEqualTo(1L);
    }
}
