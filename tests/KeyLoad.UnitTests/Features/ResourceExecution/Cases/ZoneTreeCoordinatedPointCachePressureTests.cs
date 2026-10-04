using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCachePressureTests
{
    private const int MaxEntries = 8;
    private const long IndexCharge = 1024L + (64L * MaxEntries);
    private static readonly byte[] Key = "cache/coordinated/pressure"u8.ToArray();
    private static readonly byte[] Value = [5, 10, 15];

    [Test]
    public async Task WithdrawnHelperReleasesItsOnlyIndexBeforeReplacementReservesAgain()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture(new CacheMemoryLimits
        {
            MaxRetainedBytes = IndexCharge,
            MaxRetainedEntries = 1
        });
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out var grant, out var first);
            var store = fixture.OpenStore(MaxEntries);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
            await Assert.That(control.TryApply(first)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsEqualTo(IndexCharge);
            await Assert.That(permit.TryWithdraw(grant, first.Revision)).IsTrue();
            await Assert.That(control.Retire(first.Revision)).IsTrue();
            var replacement = ZoneTreeCoordinatedPointCacheTestSupport.AcceptRenewal(permit, 2, out _);

            var result = control.TryApply(replacement);
            var snapshot = control.GetDiagnostics();

            await Assert.That(replacement).IsEqualTo(new CacheReadPermitAcceptance(2, 1, false));
            await Assert.That(result).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            await Assert.That(snapshot.Enabled).IsTrue();
            await Assert.That(snapshot.RetainedBytes).IsEqualTo(IndexCharge);
            await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsEqualTo(IndexCharge);
        });
    }

    [Test]
    public async Task RealPoolPressureCanRetryTheSameReceiptAfterOwningStoreCloses()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture(new CacheMemoryLimits
        {
            MaxRetainedBytes = IndexCharge + EntryCharge(Key.Length, Value.Length),
            MaxRetainedEntries = 1
        });
        await fixture.RunAsync(async () =>
        {
            var entryCharge = EntryCharge(Key.Length, Value.Length);
            using var firstPermit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var firstReceipt);
            using var secondPermit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var secondReceipt);
            var firstStore = fixture.OpenStore(MaxEntries);
            var secondStore = fixture.OpenStore(MaxEntries);
            ZoneTreeCoordinatedPointCacheTestSupport.Put(firstStore, Key, Value);
            ZoneTreeCoordinatedPointCacheTestSupport.Put(secondStore, Key, Value);
            var firstControl = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(firstStore, fixture, firstPermit);
            var secondControl = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(secondStore, fixture, secondPermit);
            await Assert.That(firstControl.TryApply(firstReceipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            _ = firstStore.Read(view => view.ReadOwnedValue(Key));
            var occupiedPool = fixture.Budget.GetSnapshot();
            var unavailable = secondControl.TryApply(secondReceipt);
            var whileFull = secondControl.GetDiagnostics();
            var native = secondStore.Read(view => view.ReadOwnedValue(Key));

            fixture.CloseStore(firstStore);
            var retry = secondControl.TryApply(secondReceipt);
            var ready = secondControl.GetDiagnostics();
            var filled = secondStore.Read(view => view.ReadOwnedValue(Key));
            var warm = secondStore.Read(view => view.ReadOwnedValue(Key));

            await Assert.That(occupiedPool.RetainedBytes).IsEqualTo(IndexCharge + entryCharge);
            await Assert.That(unavailable).IsEqualTo(ZoneTreePointCacheControlResult.Unavailable);
            await Assert.That(whileFull.Enabled).IsFalse();
            await Assert.That(native).IsNotNull();
            await Assert.That(native!.SequenceEqual(Value)).IsTrue();
            await Assert.That(retry).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            await Assert.That(ready.Enabled).IsTrue();
            await Assert.That(filled).IsNotNull();
            await Assert.That(filled!.SequenceEqual(Value)).IsTrue();
            await Assert.That(warm).IsNotNull();
            await Assert.That(warm!.SequenceEqual(Value)).IsTrue();
            await Assert.That(secondControl.GetDiagnostics().Hits).IsEqualTo(1L);
            await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsEqualTo(IndexCharge + entryCharge);
        });
    }

    private static long EntryCharge(int keyLength, int valueLength)
        => 320L + RoundToEight(keyLength) + RoundToEight(valueLength);

    private static long RoundToEight(int length) => (length + 7L) & ~7L;

}
