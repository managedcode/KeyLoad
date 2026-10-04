using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheDeletionTests
{
    private static readonly byte[] KeyA = "cache/delete-reinsert"u8.ToArray();
    private static readonly byte[] OriginalValue = [10, 11];
    private static readonly byte[] ReplacementValue = [99, 100];

    [Test]
    public async Task WarmDeleteMissesNativeTwiceThenDifferentReinsertIsColdAndWarmsCorrectly()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore();
        Put(store, OriginalValue);

        var original = Read(store);
        var afterWarm = store.GetPointCacheDiagnostics();
        store.Commit((transaction, _) => { transaction.Delete(KeyA); return true; });
        var absentFirst = Read(store);
        var absentSecond = Read(store);
        var afterAbsentReads = store.GetPointCacheDiagnostics();

        Put(store, ReplacementValue);
        var replacementCold = Read(store);
        var afterReplacementCold = store.GetPointCacheDiagnostics();
        var replacementWarm = Read(store);
        var afterReplacementWarm = store.GetPointCacheDiagnostics();

        await Assert.That(original).IsEquivalentTo(OriginalValue);
        await Assert.That(absentFirst).IsNull();
        await Assert.That(absentSecond).IsNull();
        await Assert.That(replacementCold).IsEquivalentTo(ReplacementValue);
        await Assert.That(replacementWarm).IsEquivalentTo(ReplacementValue);
        await Assert.That(afterWarm.NativeLookups).IsEqualTo(1L);
        await Assert.That(afterWarm.NativeMisses).IsEqualTo(0L);
        await Assert.That(afterWarm.Admissions).IsEqualTo(1L);
        await Assert.That(afterAbsentReads.Hits).IsEqualTo(afterWarm.Hits);
        await Assert.That(afterAbsentReads.NativeLookups - afterWarm.NativeLookups).IsEqualTo(2L);
        await Assert.That(afterAbsentReads.NativeMisses - afterWarm.NativeMisses).IsEqualTo(2L);
        await Assert.That(afterAbsentReads.Admissions).IsEqualTo(afterWarm.Admissions);
        await Assert.That(afterAbsentReads.LiveEntries).IsEqualTo(0);
        await Assert.That(afterReplacementCold.Hits).IsEqualTo(afterAbsentReads.Hits);
        await Assert.That(afterReplacementCold.NativeLookups - afterAbsentReads.NativeLookups).IsEqualTo(1L);
        await Assert.That(afterReplacementCold.NativeMisses).IsEqualTo(afterAbsentReads.NativeMisses);
        await Assert.That(afterReplacementCold.Admissions - afterAbsentReads.Admissions).IsEqualTo(1L);
        await Assert.That(afterReplacementCold.LiveEntries).IsEqualTo(1);
        await Assert.That(afterReplacementWarm.Hits - afterReplacementCold.Hits).IsEqualTo(1L);
        await Assert.That(afterReplacementWarm.NativeLookups).IsEqualTo(afterReplacementCold.NativeLookups);
        await Assert.That(afterReplacementWarm.NativeMisses).IsEqualTo(afterReplacementCold.NativeMisses);
        await Assert.That(afterReplacementWarm.Admissions).IsEqualTo(afterReplacementCold.Admissions);
    }

    private static byte[]? Read(ZoneTreeStore store)
        => store.Read(view => view.ReadOwnedValue(KeyA));

    private static void Put(ZoneTreeStore store, byte[] value)
        => store.Commit((transaction, _) => { transaction.Put(KeyA, value); return true; });
}
