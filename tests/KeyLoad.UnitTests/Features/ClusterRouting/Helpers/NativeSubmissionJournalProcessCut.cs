using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record NativeSubmissionJournalProcessCut(long CanonicalPosition, long ReplicaPosition,
    byte[] CanonicalRows, byte[] ReplicaRows)
{
    private const int MaximumRecords = 256;
    private const long MaximumBytes = 131072;

    internal static NativeSubmissionJournalProcessCut Read(NativeSubmissionJournalProcessFixture fixture)
        => WithCanonical(fixture.Canonical, canonical => WithReplica(fixture, replica =>
        {
            long examined = 0;
            var records = 0;
            var canonicalRows = ReadRows(canonical, Observe, ref records);
            var replicaRows = ReadRows(replica, Observe, ref records);
            return new NativeSubmissionJournalProcessCut(canonical.Position, replica.Position, canonicalRows, replicaRows);

            void Observe(long bytes)
            {
                if (bytes < 0 || bytes > MaximumBytes - examined)
                { throw new InvalidDataException("The complete original both-store cut exceeds its byte budget."); }
                examined += bytes;
            }
        }));

    internal static T WithCanonical<T>(C1OutcomeInspectionFixture fixture, Func<ZoneTreeStore, T> body)
        => UseStore(() => ZoneTreeExistingStore.Open(new(fixture.DirectoryPath)
        { Incarnation = fixture.Incarnation }, fixture.Identity.NodeId, UnitExecutionOptions.StorageExecution()), body);

    private static T WithReplica<T>(NativeSubmissionJournalProcessFixture fixture, Func<ZoneTreeStore, T> body)
        => UseStore(() => ZoneTreeExistingStore.Open(new(fixture.ReplicaDirectory)
        { Incarnation = fixture.Canonical.Incarnation }, fixture.ReplicaIdentity.NodeId,
            UnitExecutionOptions.StorageExecution()), body);

    private static T UseStore<T>(Func<ZoneTreeStore> open, Func<ZoneTreeStore, T> body)
    {
        var failures = new List<Exception>();
        ZoneTreeStore? store = null;
        T result = default!;
        ServerFailureObserver.Observe(() => { store = open(); result = body(store); }, failures);
        ServerFailureObserver.Observe(() => store?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static byte[] ReadRows(ZoneTreeStore store, StorageReadObserver observe, ref int records)
    {
        var remaining = MaximumRecords - records;
        if (remaining <= 0)
        { throw new InvalidDataException("The complete both-store record budget is exhausted."); }
        var rows = new List<KeyValueRecord>();
        store.Read(view =>
        {
            var scan = view.VisitRange([], remaining, (key, value) =>
            { rows.Add(new(key.ToArray(), value.ToArray())); return true; }, observer: observe,
                cancellationToken: TestContext.Current!.Execution.CancellationToken);
            if (scan.HasMore || scan.StoppedByVisitor)
            { throw new InvalidDataException("The complete original both-store cut exceeds its record budget."); }
            return true;
        });
        records += rows.Count;
        return NativeSerialization.Serialize(new ScanPage([.. rows], false));
    }

    internal async Task RequireUnchangedAsync(NativeSubmissionJournalProcessFixture fixture)
    {
        var after = Read(fixture);
        await Assert.That(after.CanonicalPosition).IsEqualTo(CanonicalPosition);
        await Assert.That(after.ReplicaPosition).IsEqualTo(ReplicaPosition);
        await Assert.That(after.CanonicalRows.AsSpan().SequenceEqual(CanonicalRows)).IsTrue();
        await Assert.That(after.ReplicaRows.AsSpan().SequenceEqual(ReplicaRows)).IsTrue();
    }

    internal static void ReplaceEntry(NativeSubmissionJournalProcessFixture fixture, byte[] bytes)
        => WithReplica(fixture, store => store.Commit((tx, _) =>
        { tx.Put(ReplicaProtocol.EntryStorageKey(fixture.Seed.Entry.Index), bytes); return true; }));

    public override string ToString() => nameof(NativeSubmissionJournalProcessCut);
}
