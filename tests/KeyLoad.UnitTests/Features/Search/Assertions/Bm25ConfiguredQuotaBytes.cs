using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class Bm25ConfiguredQuotaBytes
{
    internal static long Retained(TestDatabase database)
        => database.Store.Read(view =>
        {
            var page = view.Scan(Bm25CorpusKeys.Prefix(database.Partition), database.Database.Limits.MaxScanRecords, null);
            if (page.HasMore)
            { throw new InvalidOperationException("The complete actual corpus family exceeded its original bound."); }
            return page.Records.Sum(row => checked((long)row.Key.Length + row.Value.Length));
        });

    internal static long Candidate(ReplicatedOperation operation)
    {
        var current = Bm25ConfiguredQuotaFlow.Phase(operation).Current;
        return checked((long)Bm25CorpusKeys.Current(current).Length + NativeSerialization.Serialize(current).Length);
    }

    internal static async Task RequireStoredAsync(TestDatabase database, ReplicatedOperation operation, OperationResult result)
    {
        await Assert.That(result.Error).IsNull();
        var phase = Bm25ConfiguredQuotaFlow.Phase(operation);
        var receipt = result.Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(operation.Id);
        await Assert.That(receipt.Token.Position).IsEqualTo(database.ReadRetainedEntry(operation.Id).Index);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(database.Store.Identity.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Durability).IsEqualTo(database.Database.Durability);
        var actual = database.Store.Read(view => view.ReadOwnedValue(Bm25CorpusKeys.Current(phase.Current)))
            ?? throw new InvalidOperationException("The genuinely published corpus head is missing.");
        await ReplicaSubmissionOutcomeFlow.RequireBytesAsync(NativeSerialization.Serialize(phase.Current), actual);
        await ReplicaSubmissionOutcomeFlow.RequireResultAsync(result, database.Database.ResolveSubmissionOutcome(
            database.ReadRetainedEntry(operation.Id).Operation!));
    }
}
