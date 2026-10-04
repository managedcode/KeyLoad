using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheCoherenceTests
{
    private static readonly byte[] KeyA = "cache/a"u8.ToArray();
    private static readonly byte[] KeyB = "cache/b"u8.ToArray();
    private static readonly byte[] ValueA = [10, 11];
    private static readonly byte[] ValueB = [20, 21];

    [Test]
    public async Task ChangedApplyInvalidatesAWhileUnrelatedWarmBRemains()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore();
        CommitPair(store);
        Warm(store, KeyA);
        Warm(store, KeyB);
        var before = store.GetPointCacheDiagnostics();

        Put(store, KeyA, [12, 13]);
        AssertValue(store, KeyB, ValueB);
        AssertValue(store, KeyA, [12, 13]);
        var after = store.GetPointCacheDiagnostics();

        await Assert.That(after.Hits - before.Hits).IsEqualTo(1L);
        await Assert.That(after.NativeLookups - before.NativeLookups).IsEqualTo(1L);
        await Assert.That(after.Admissions - before.Admissions).IsEqualTo(1L);
        await Assert.That(after.LiveEntries).IsEqualTo(2);
    }

    [Test]
    public async Task StagedPutDeleteResetAndRejectedCompilerCannotWarmOrChangeCommittedValues()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore();
        CommitPair(store);
        Warm(store, KeyA);
        var stagedPut = "cache/staged"u8.ToArray();
        var before = store.GetPointCacheDiagnostics();

        store.Commit((tx, _) => StageValuesAndReset(tx, stagedPut));
        var afterReset = store.GetPointCacheDiagnostics();
        Func<IAtomicTransaction, long, bool> rejectedCompiler = (tx, _) =>
        {
            tx.Put(stagedPut, [88]);
            throw new InvalidOperationException("The staged compiler was rejected.");
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Commit(rejectedCompiler));
        var afterReject = store.GetPointCacheDiagnostics();
        AssertValue(store, KeyA, ValueA);
        AssertValue(store, stagedPut, null);

        await Assert.That(afterReset.Admissions).IsEqualTo(before.Admissions);
        await Assert.That(afterReject.Admissions).IsEqualTo(before.Admissions);
        await Assert.That(afterReject.Hits).IsEqualTo(before.Hits);
        await Assert.That(afterReject.NativeLookups - before.NativeLookups).IsEqualTo(0L);
    }

    [Test]
    public async Task SameCutCompactionRetainsWarmEntriesButSnapshotReplacementAndReopenAreCold()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var incarnation = Guid.NewGuid();
        var signingKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        var store = fixture.OpenStore(incarnation: incarnation, signingKey: signingKey);
        Put(store, KeyA, ValueA);
        Warm(store, KeyA);
        store.Compact();
        var afterCompaction = store.GetPointCacheDiagnostics();
        AssertValue(store, KeyA, ValueA);
        await Assert.That(store.GetPointCacheDiagnostics().Hits).IsEqualTo(afterCompaction.Hits + 1);

        var source = fixture.OpenStore(incarnation: incarnation, signingKey: signingKey);
        Put(source, KeyB, ValueB);
        var snapshotPath = Path.Combine(fixture.DirectoryOf(source), "replacement.snapshot");
        var snapshot = source.CreateSnapshot(snapshotPath);
        var oldGeneration = store.Identity.ReadGeneration;
        store.InstallSnapshot(snapshotPath, snapshot.AppliedPosition);
        await Assert.That(store.Identity.ReadGeneration).IsEqualTo(oldGeneration + 1);
        await Assert.That(store.GetPointCacheDiagnostics().LiveEntries).IsEqualTo(0);
        var configuredIndexCharge = new ZoneTreePointCacheOptions(fixture.Budget) { MaxEntries = 8 }.IndexChargeBytes;
        await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsGreaterThanOrEqualTo(configuredIndexCharge);
        AssertValue(store, KeyA, null);
        AssertValue(store, KeyB, ValueB);
        var directory = fixture.DirectoryOf(store);
        fixture.CloseStore(store);

        var reopened = fixture.ReopenStore(directory);
        var cold = reopened.GetPointCacheDiagnostics();
        AssertValue(reopened, KeyB, ValueB);
        var reopenedRead = reopened.GetPointCacheDiagnostics();
        await Assert.That(cold.Hits).IsEqualTo(0L);
        await Assert.That(reopenedRead.NativeLookups).IsEqualTo(1L);
        await Assert.That(reopenedRead.Admissions).IsEqualTo(1L);
    }

    private static bool StageValuesAndReset(IAtomicTransaction transaction, byte[] stagedPut)
    {
        transaction.Put(KeyA, [99]);
        if (!transaction.ReadValue(KeyA, AssertStagedValue))
        {
            throw new InvalidOperationException("Staged replacement did not shadow the warm committed value.");
        }

        transaction.Put(stagedPut, [99]);
        if (!transaction.ReadValue(stagedPut, AssertStagedValue))
        {
            throw new InvalidOperationException("Staged positive value was not readable.");
        }

        transaction.Delete(KeyA);
        if (transaction.ReadOwnedValue(KeyA) is not null)
        {
            throw new InvalidOperationException("Staged tombstone was not visible.");
        }

        transaction.Reset();
        return true;
    }

    private static void CommitPair(ZoneTreeStore store)
        => store.Commit((tx, _) => { tx.Put(KeyA, ValueA); tx.Put(KeyB, ValueB); return true; });

    private static void Put(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((tx, _) => { tx.Put(key, value); return true; });

    private static void Warm(ZoneTreeStore store, byte[] key)
        => _ = store.Read(view => view.ReadOwnedValue(key));

    private static void AssertValue(ZoneTreeStore store, byte[] key, byte[]? expected)
    {
        var actual = store.Read(view => view.ReadOwnedValue(key));
        if (expected is null ? actual is not null : actual is null || !actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException("The real ZoneTree read returned unexpected bytes.");
        }
    }

    private static void AssertStagedValue(ReadOnlySpan<byte> value)
    {
        if (!value.SequenceEqual(new byte[] { 99 }))
        {
            throw new InvalidOperationException("Staged positive bytes changed.");
        }
    }
}
