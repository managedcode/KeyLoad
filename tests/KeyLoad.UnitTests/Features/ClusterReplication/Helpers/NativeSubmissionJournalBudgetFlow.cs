using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterReplication;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests;

internal static class NativeSubmissionJournalBudgetFlow
{
    private const int MaximumOwnerBytes = 32768;
    private static readonly byte[] TinyKey = KeyCodec.Encode("journal-budget-owned-row");
    private static readonly byte[] TinyValue = new byte[64];

    internal static async Task RunOwnedAsync(bool exhaustCanonical)
    {
        var failures = new List<Exception>();
        ReplicaAppendBoundaryFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => RunAsync(fixture, exhaustCanonical), failures);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunAsync(ReplicaAppendBoundaryFixture fixture, bool exhaustCanonical)
    {
        var original = Append(fixture, "shared-budget-original");
        fixture.Canonical.Store.Commit((tx, _) => { tx.Put(TinyKey, TinyValue); return true; });
        var before = NativeSubmissionJournalBudgetCut.Read(fixture);
        var token = TestContext.Current!.Execution.CancellationToken;
        var budget = new C1SubmissionJournalInspectionReadBudget(token);
        using var inspection = C1SubmissionJournalInspectionDatabase.Open(fixture.Canonical.Store, budget, token);
        var database = inspection.Database;
        if (exhaustCanonical)
        {
            await RequireSharedRefusalAsync(fixture, database, original, budget);
        }
        else
        {
            await RequireReadAsync(fixture, database, original, budget);
        }
        await before.RequireUnchangedAsync(fixture);
        await RequireFreshReadAsync(fixture, original);
        var following = Append(fixture, "shared-budget-following");
        await RequireFreshReadAsync(fixture, following);
    }

    private static async Task RequireSharedRefusalAsync(ReplicaAppendBoundaryFixture fixture,
        DatabaseEngine database, ReplicaEntry original, C1SubmissionJournalInspectionReadBudget budget)
    {
        var rowBytes = Measure(fixture.Canonical.Store, TinyKey);
        var journalBytes = Measure(fixture.Store, ReplicaProtocol.StateStorageKey)
            + Measure(fixture.Store, ReplicaBenchmarkMembership.StorageKey)
            + Measure(fixture.Store, ReplicaProtocol.EntryStorageKey(original.Index));
        await Assert.That(rowBytes > 0 && journalBytes > rowBytes && journalBytes <= MaximumOwnerBytes).IsTrue();
        var admitted = ExhaustCanonical(database.Store, rowBytes);
        await Assert.That(admitted > 0).IsTrue();
        var failure = Assert.ThrowsExactly<InvalidDataException>(() => NativeSubmissionJournalRead.Run(fixture.Store,
            database, fixture.Configuration, original.Index, original.Term,
            TestContext.Current!.Execution.CancellationToken, budget.Charge));
        await Assert.That(failure.Message).IsEqualTo(C1SubmissionJournalInspectionProtocol.Invalid);
    }

    private static long ExhaustCanonical(IAtomicStore store, long rowBytes)
    {
        long admittedBytes = 0;
        var successful = 0;
        var denied = false;
        var attempts = checked((int)(MaximumOwnerBytes / rowBytes) + 1);
        for (var attempt = 0; attempt < attempts && !denied; attempt++)
        {
            var callback = false;
            try
            {
                byte[]? actual = null;
                var found = store.Read(view => view.ReadValue(TinyKey,
                    bytes => { callback = true; actual = bytes.ToArray(); },
                    bytes => admittedBytes = checked(admittedBytes + bytes)));
                if (!found || actual is null || !actual.AsSpan().SequenceEqual(TinyValue))
                {
                    throw new InvalidOperationException("The original tiny native row changed.");
                }
                successful++;
            }
            catch (InvalidDataException failure)
            {
                if (failure.Message != C1SubmissionJournalInspectionProtocol.Invalid || callback)
                {
                    throw;
                }
                denied = true;
            }
        }
        if (!denied || admittedBytes != successful * rowBytes)
        {
            throw new InvalidOperationException("The actual shared native read budget did not refuse before copy.");
        }
        return admittedBytes;
    }

    private static long Measure(global::KeyLoad.Storage.ZoneTree.ZoneTreeStore store, byte[] key)
    {
        long measured = 0;
        byte[]? actual = null;
        var found = store.Read(view => view.ReadValue(key, bytes => actual = bytes.ToArray(),
            bytes => measured = checked(measured + bytes)));
        if (!found || actual is null || actual.Length == 0 || measured <= 0 || measured > 8192)
        {
            throw new InvalidOperationException("The real original native row could not be independently measured.");
        }
        return measured;
    }

    private static async Task RequireFreshReadAsync(ReplicaAppendBoundaryFixture fixture, ReplicaEntry entry)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var budget = new C1SubmissionJournalInspectionReadBudget(token);
        using var inspection = C1SubmissionJournalInspectionDatabase.Open(fixture.Canonical.Store, budget, token);
        var database = inspection.Database;
        await RequireReadAsync(fixture, database, entry, budget);
    }

    private static async Task RequireReadAsync(ReplicaAppendBoundaryFixture fixture, DatabaseEngine database,
        ReplicaEntry entry, C1SubmissionJournalInspectionReadBudget budget)
    {
        var result = NativeSubmissionJournalRead.Run(fixture.Store, database, fixture.Configuration,
            entry.Index, entry.Term, TestContext.Current!.Execution.CancellationToken, budget.Charge);
        await Assert.That(result.NodeId).IsEqualTo(fixture.Store.Identity.NodeId);
        await Assert.That(result.Incarnation).IsEqualTo(fixture.Configuration.Incarnation);
        await Assert.That(result.Position).IsEqualTo(fixture.Store.Position);
        await Assert.That(result.NativeHardState.AsSpan().SequenceEqual(ReplicaProtocolCodec.Serialize(fixture.Log.State))).IsTrue();
        await Assert.That(result.NativeEntry.AsSpan().SequenceEqual(ReplicaProtocolCodec.Serialize(entry))).IsTrue();
        await Assert.That(NativeSerialization.Serialize(result.Entry).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(entry))).IsTrue();
    }

    private static ReplicaEntry Append(ReplicaAppendBoundaryFixture fixture, string document)
    {
        var entry = fixture.Create(document, checked(fixture.Log.State.LastIndex + 1));
        fixture.Log.Append([entry]);
        fixture.Log.Commit(entry.Index);
        return entry;
    }
}
