using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>Actual retained native journal admission precedes copying and decoding; this is not RF3 or first-Admit leader proof.</summary>
internal sealed class ReplicaAdmittedJournalReadTests
{
    private const long FirstIndex = 1;
    private const long CurrentTerm = 1;
    private const long NoOwnedCopies = 0;
    private const long OneLookup = 1;
    private const long InitialAppliedPosition = 0;
    private const long InitialExaminedBytes = 0;

    [Test]
    public async Task ExhaustedOriginalReadGrantRefusesRetainedEntryWithoutStateChangeThenHealthyReadReturnsOriginal()
    {
        await using var fixture = new ReplicaLifecycleFixture();
        fixture.Log.SaveTermAndVote(CurrentTerm, fixture.Configuration.LocalId);
        var original = new ReplicaEntry(FirstIndex, CurrentTerm, null);
        fixture.Log.Append([original]);
        var before = fixture.Log.State;
        var settings = UnitExecutionOptions.DatabaseLimits();
        var work = new ReadExecutionBudget(settings, TimeProvider.System,
            TestContext.Current!.Execution.CancellationToken);
        work.ChargeBytes(settings.Value.MaxQueryReadBytes);
        var refused = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Log.ReadEntry(FirstIndex, work.ChargeBytes));
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(fixture.Log.State).IsEqualTo(before);
        var healthy = new ReadExecutionBudget(settings, TimeProvider.System,
            TestContext.Current!.Execution.CancellationToken);
        var observed = fixture.Log.ReadEntry(FirstIndex, healthy.ChargeBytes);
        await Assert.That(observed).IsEqualTo(original);
        await Assert.That(fixture.Log.State).IsEqualTo(before);
        await Assert.That(fixture.Database.LastApplied).IsEqualTo(InitialAppliedPosition);
        var ordinary = fixture.Log.Read(FirstIndex, fixture.Configuration.MaxAppendEntries,
            fixture.Configuration.MaxAppendBytes);
        await Assert.That(ordinary.Single()).IsEqualTo(original);
    }

    [Test]
    public async Task ActualMissingRetainedLookupIsChargedBeforeCorruptionThenExactNativeRowRepairRestoresRead()
    {
        await using var fixture = new ReplicaLifecycleFixture();
        fixture.Log.SaveTermAndVote(CurrentTerm, fixture.Configuration.LocalId);
        var original = new ReplicaEntry(FirstIndex, CurrentTerm, null);
        fixture.Log.Append([original]);
        var key = ReplicaProtocol.EntryStorageKey(FirstIndex);
        var bytes = fixture.ReplicaStore.Read(view => view.ReadOwnedValue(key))!;
        var before = fixture.Log.State;
        fixture.ReplicaStore.Commit((transaction, _) => { transaction.Delete(key); return true; });
        var counters = fixture.ReplicaStore.GetReadDiagnostics();
        var examined = InitialExaminedBytes;
        var refused = Assert.ThrowsExactly<KeyLoadException>(() =>
            fixture.Log.ReadEntry(FirstIndex, size => examined += size));
        var after = fixture.ReplicaStore.GetReadDiagnostics();
        await Assert.That(refused.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(examined).IsEqualTo((long)key.Length);
        await Assert.That(after.BorrowedPointLookups - counters.BorrowedPointLookups).IsEqualTo(OneLookup);
        await Assert.That(after.OwnedPointLookups - counters.OwnedPointLookups).IsEqualTo(NoOwnedCopies);
        await Assert.That(fixture.Log.State).IsEqualTo(before);
        fixture.ReplicaStore.Commit((transaction, _) => { transaction.Put(key, bytes); return true; });
        examined = InitialExaminedBytes;
        var healthy = fixture.Log.ReadEntry(FirstIndex, size => examined += size);
        await Assert.That(healthy).IsEqualTo(original);
        await Assert.That(examined).IsEqualTo(checked((long)key.Length + bytes.Length));
        await Assert.That(fixture.Log.State).IsEqualTo(before);
        await Assert.That(fixture.ReplicaStore.Read(view => view.ReadOwnedValue(key))!.AsSpan()
            .SequenceEqual(bytes)).IsTrue();
    }
}
