using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeSubmissionOutcomeProcessCut(long Position, byte[] Records)
{
    private const int MaximumRecords = 256;
    private const long MaximumBytes = 131072;

    internal static NativeSubmissionOutcomeProcessCut Read(C1OutcomeInspectionFixture fixture)
    {
        fixture.CloseStore();
        using var store = ZoneTreeExistingStore.Open(new(fixture.DirectoryPath) { Incarnation = fixture.Incarnation },
            fixture.Identity.NodeId, UnitExecutionOptions.StorageExecution());
        return store.Read(view =>
        {
            var page = view.Scan([], MaximumRecords);
            if (page.HasMore || page.Records.Sum(row => (long)row.Key.Length + row.Value.Length) > MaximumBytes)
            { throw new InvalidOperationException("The complete original inspection cut exceeded its bound."); }
            return new NativeSubmissionOutcomeProcessCut(store.Position, NativeSerialization.Serialize(page));
        });
    }

    internal async Task RequireUnchangedAsync(C1OutcomeInspectionFixture fixture)
    {
        var after = Read(fixture);
        await Assert.That(after.Position).IsEqualTo(Position);
        await Assert.That(after.Records.AsSpan().SequenceEqual(Records)).IsTrue();
    }

    internal static void ReplaceOwnedRow(C1OutcomeInspectionFixture fixture, byte[] key, byte[] value)
    {
        fixture.CloseStore();
        using var store = ZoneTreeExistingStore.Open(new(fixture.DirectoryPath) { Incarnation = fixture.Incarnation },
            fixture.Identity.NodeId, UnitExecutionOptions.StorageExecution());
        store.Commit((transaction, _) => { transaction.Put(key, value); return true; });
    }

    public override string ToString() => nameof(NativeSubmissionOutcomeProcessCut);
}
