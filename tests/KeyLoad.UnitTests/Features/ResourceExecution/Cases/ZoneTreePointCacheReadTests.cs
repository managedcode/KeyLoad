using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheReadTests
{
    private const string KeyText = "cache/raw/principal/1";
    private const long OwnerIndexCharge = 1024 + 64 * 8;
    private const long EntryMetadataBytes = 320;
    private static readonly byte[] KeyBytes = "cache/raw/principal/1"u8.ToArray();
    private static readonly byte[] ValueBytes = [0, 1, 2, 255];

    [Test]
    public async Task ColdOwnedReadWarmsExactKeyAndWarmOwnedReadReturnsIndependentBytes()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore();
        Put(store, KeyBytes, ValueBytes);

        var logicalBefore = store.GetReadDiagnostics();
        var cold = store.Read(view => view.ReadOwnedValue(KeyBytes));
        var afterCold = store.GetPointCacheDiagnostics();
        cold![0] = 77;
        var warm = store.Read(view => view.ReadOwnedValue(Key(KeyText)));
        var afterWarm = store.GetPointCacheDiagnostics();
        await Assert.That(warm).IsEquivalentTo(ValueBytes);
        warm![0] = 88;
        var repeated = store.Read(view => view.ReadOwnedValue(Key(KeyText)));
        var afterRepeated = store.GetPointCacheDiagnostics();
        var logicalAfter = store.GetReadDiagnostics();

        await Assert.That(repeated).IsEquivalentTo(ValueBytes);
        await Assert.That(afterCold.NativeLookups).IsEqualTo(1L);
        await Assert.That(afterCold.Admissions).IsEqualTo(1L);
        await Assert.That(afterCold.RetainedBytes).IsEqualTo(OwnerIndexCharge
            + EntryMetadataBytes + RoundToEight(KeyBytes.Length) + RoundToEight(ValueBytes.Length));
        await Assert.That(afterWarm.Hits).IsEqualTo(1L);
        await Assert.That(afterWarm.NativeLookups).IsEqualTo(1L);
        await Assert.That(afterWarm.RetainedBytes).IsEqualTo(afterCold.RetainedBytes);
        await Assert.That(afterRepeated.Hits).IsEqualTo(2L);
        await Assert.That(afterRepeated.NativeLookups).IsEqualTo(1L);
        await Assert.That(afterRepeated.RetainedBytes).IsEqualTo(afterCold.RetainedBytes);
        await Assert.That(logicalAfter.OwnedPointLookups - logicalBefore.OwnedPointLookups).IsEqualTo(3L);
        await Assert.That(logicalAfter.PointExaminedBytes - logicalBefore.PointExaminedBytes)
            .IsEqualTo(3L * (KeyBytes.Length + ValueBytes.Length));
    }

    [Test]
    public async Task BorrowedWarmReadPreservesLogicalObserverChargeAndCapturedKeyMutationCannotRebindFill()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore();
        Put(store, KeyBytes, ValueBytes);
        var suppliedKey = KeyTextBytes();
        long observed = 0;
        var logicalBefore = store.GetReadDiagnostics();

        store.Read(view => view.ReadValue(suppliedKey, _ => { }, amount =>
        {
            observed += amount;
            suppliedKey[0] ^= 0x20;
        }));
        var first = store.GetPointCacheDiagnostics();
        var value = store.Read(view => CopyBorrowed(view, Key(KeyText), amount => observed += amount));
        var second = store.GetPointCacheDiagnostics();
        var logicalAfter = store.GetReadDiagnostics();

        await Assert.That(value).IsEquivalentTo(ValueBytes);
        await Assert.That(observed).IsEqualTo(2L * (KeyBytes.Length + ValueBytes.Length));
        await Assert.That(first.NativeLookups).IsEqualTo(1L);
        await Assert.That(second.Hits).IsEqualTo(1L);
        await Assert.That(second.NativeLookups).IsEqualTo(1L);
        await Assert.That(logicalAfter.BorrowedPointLookups - logicalBefore.BorrowedPointLookups).IsEqualTo(2L);
        await Assert.That(logicalAfter.PointExaminedBytes - logicalBefore.PointExaminedBytes)
            .IsEqualTo(2L * (KeyBytes.Length + ValueBytes.Length));
    }

    [Test]
    public async Task EmptyPositiveValueWarmsWhileMissingAndTombstoneRemainNativeMisses()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore();
        var emptyKey = Key("cache/empty");
        var deletedKey = Key("cache/deleted");
        store.Commit((tx, _) =>
        {
            tx.Put(emptyKey, []);
            tx.Put(deletedKey, [9]);
            return true;
        });
        store.Commit((tx, _) => { tx.Delete(deletedKey); return true; });

        var emptyFirst = store.Read(view => view.ReadOwnedValue(emptyKey));
        var emptySecond = store.Read(view => view.ReadOwnedValue(Key("cache/empty")));
        var absentFirst = store.Read(view => view.ReadOwnedValue(deletedKey));
        var absentSecond = store.Read(view => view.ReadOwnedValue(Key("cache/deleted")));
        var snapshot = store.GetPointCacheDiagnostics();

        await Assert.That(emptyFirst).IsNotNull();
        await Assert.That(emptyFirst).IsEquivalentTo(Array.Empty<byte>());
        await Assert.That(emptySecond).IsEquivalentTo(Array.Empty<byte>());
        await Assert.That(absentFirst).IsNull();
        await Assert.That(absentSecond).IsNull();
        await Assert.That(snapshot.Admissions).IsEqualTo(1L);
        await Assert.That(snapshot.NativeMisses).IsEqualTo(2L);
        await Assert.That(snapshot.Hits).IsEqualTo(1L);
    }

    private static byte[]? CopyBorrowed(IKeyValueView view, byte[] key, StorageReadObserver observer)
    {
        byte[]? result = null;
        view.ReadValue(key, value => result = value.ToArray(), observer);
        return result;
    }

    private static void Put(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((tx, _) => { tx.Put(key, value); return true; });

    private static byte[] Key(string value) => System.Text.Encoding.UTF8.GetBytes(value);
    private static byte[] KeyTextBytes() => Key(KeyText);
    private static long RoundToEight(int length) => (length + 7L) & ~7L;
}
