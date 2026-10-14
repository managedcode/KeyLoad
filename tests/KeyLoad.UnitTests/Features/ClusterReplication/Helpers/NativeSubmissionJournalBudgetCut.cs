using KeyLoad.Storage;

namespace KeyLoad.UnitTests;

internal sealed record NativeSubmissionJournalBudgetCut(long CanonicalPosition, long ReplicaPosition,
    byte[] CanonicalRows, byte[] ReplicaRows)
{
    internal static NativeSubmissionJournalBudgetCut Read(ReplicaAppendBoundaryFixture fixture)
    {
        long examined = 0;
        var records = 0;
        var canonical = ReadRows(fixture.Canonical.Store, Observe, ref records);
        var replica = ReadRows(fixture.Store, Observe, ref records);
        return new(fixture.Canonical.Store.Position, fixture.Store.Position, canonical, replica);

        void Observe(long bytes)
        {
            if (bytes < 0 || bytes > 131072 - examined)
            {
                throw new InvalidDataException("The complete both-store byte bound was exceeded.");
            }
            examined += bytes;
        }
    }

    private static byte[] ReadRows(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, StorageReadObserver observe, ref int records)
    {
        var remaining = 256 - records;
        if (remaining <= 0)
        {
            throw new InvalidDataException("The complete both-store row bound was exceeded.");
        }
        var rows = new List<KeyValueRecord>();
        store.Read(view =>
        {
            var result = view.VisitRange([], remaining, (key, value) =>
            {
                rows.Add(new(key.ToArray(), value.ToArray()));
                return true;
            }, observer: observe,
                cancellationToken: TestContext.Current!.Execution.CancellationToken);
            if (result.HasMore || result.StoppedByVisitor)
            {
                throw new InvalidDataException("The complete native cut cannot be truncated.");
            }
            return true;
        });
        records += rows.Count;
        return NativeSerialization.Serialize(new ScanPage([.. rows], false));
    }

    internal async Task RequireUnchangedAsync(ReplicaAppendBoundaryFixture fixture)
    {
        var after = Read(fixture);
        await Assert.That(after.CanonicalPosition).IsEqualTo(CanonicalPosition);
        await Assert.That(after.ReplicaPosition).IsEqualTo(ReplicaPosition);
        await Assert.That(after.CanonicalRows.AsSpan().SequenceEqual(CanonicalRows)).IsTrue();
        await Assert.That(after.ReplicaRows.AsSpan().SequenceEqual(ReplicaRows)).IsTrue();
    }

    public override string ToString() => nameof(NativeSubmissionJournalBudgetCut);
}
