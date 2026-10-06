using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheCapacityTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Test]
    public async Task OversizedKeyValueAndUnavailableIndexReturnNativeBytesWithoutRetention()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore(maxValueBytes: 1, maxKeyBytes: 8);
        var key = "v"u8.ToArray();
        var value = "oversized-value"u8.ToArray();
        Put(store, key, value);
        var longKey = "oversized-key"u8.ToArray();
        Put(store, longKey, [9]);

        var first = store.Read(view => view.ReadOwnedValue(key));
        var longKeyValue = store.Read(view => view.ReadOwnedValue(longKey));
        var afterOversized = store.GetPointCacheDiagnostics();
        using var limitedPool = new ZoneTreePointCacheFileFixture(new CacheMemoryLimits { MaxRetainedBytes = 1 });
        var unavailable = limitedPool.OpenStore();
        try
        {
            Put(unavailable, key, value);
            var second = unavailable.Read(view => view.ReadOwnedValue(key));
            var noIndex = unavailable.GetPointCacheDiagnostics();
            await Assert.That(second).IsEquivalentTo(value);
            await Assert.That(noIndex.NativeLookups).IsEqualTo(1L);
            await Assert.That(noIndex.Admissions).IsEqualTo(0L);
            await Assert.That(noIndex.ReadBypasses).IsGreaterThan(0L);
            await Assert.That(limitedPool.Budget.GetSnapshot().RetainedBytes).IsEqualTo(0L);
        }
        finally
        {
            limitedPool.Dispose();
        }

        await Assert.That(first).IsEquivalentTo(value);
        await Assert.That(longKeyValue).IsEquivalentTo(new byte[] { 9 });
        await Assert.That(afterOversized.NativeLookups).IsEqualTo(2L);
        await Assert.That(afterOversized.AdmissionBypasses).IsEqualTo(2L);
        await Assert.That(afterOversized.ReadBypasses).IsEqualTo(1L);
        await Assert.That(afterOversized.RetainedBytes).IsGreaterThan(0L);
    }

    [Test]
    public async Task TwoStoresShareTheExactPoolEntryCapAndResumeAdmissionAfterOwnerDisposal()
    {
        using var fixture = new ZoneTreePointCacheFileFixture(new CacheMemoryLimits { MaxRetainedEntries = 1 });
        var first = fixture.OpenStore(maxEntries: 2);
        var second = fixture.OpenStore(maxEntries: 2);
        var key = "shared-pool"u8.ToArray();
        Put(first, key, [1]);
        Put(second, key, [2]);

        _ = first.Read(view => view.ReadOwnedValue(key));
        var firstSnapshot = first.GetPointCacheDiagnostics();
        var secondValue = second.Read(view => view.ReadOwnedValue(key));
        var secondSnapshot = second.GetPointCacheDiagnostics();
        await Assert.That(secondValue).IsEquivalentTo(new byte[] { 2 });
        await Assert.That(fixture.Budget.GetSnapshot().RetainedEntries).IsEqualTo(1);
        await Assert.That(secondSnapshot.AdmissionBypasses).IsEqualTo(1L);
        await Assert.That(secondSnapshot.Hits).IsEqualTo(0L);

        fixture.CloseStore(first);
        var warmed = second.Read(view => view.ReadOwnedValue(key));
        var afterRelease = second.GetPointCacheDiagnostics();
        await Assert.That(warmed).IsEquivalentTo(new byte[] { 2 });
        await Assert.That(afterRelease.Admissions).IsEqualTo(1L);
        await Assert.That(afterRelease.Hits).IsEqualTo(0L);
        await Assert.That(firstSnapshot.Admissions).IsEqualTo(1L);
    }

    [Test]
    public async Task ClearAndDisableKeepIndexChargeUntilActualStoreDisposal()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore(maxEntries: 2);
        var key = "clear-charge"u8.ToArray();
        Put(store, key, [7]);
        _ = store.Read(view => view.ReadOwnedValue(key));
        var indexCharge = new ZoneTreePointCacheOptions(fixture.Budget) { MaxEntries = 2 }.IndexChargeBytes;

        store.Commit((tx, _) => { tx.Delete(key); return true; });
        var afterClear = store.GetPointCacheDiagnostics();
        store.DisablePointCache();
        var afterDisable = fixture.Budget.GetSnapshot();

        await Assert.That(afterClear.LiveEntries).IsEqualTo(0);
        await Assert.That(afterClear.RetainedBytes).IsGreaterThanOrEqualTo(indexCharge);
        await Assert.That(afterDisable.RetainedBytes).IsGreaterThanOrEqualTo(indexCharge);
        fixture.CloseStore(store);
        await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsEqualTo(0L);
    }

    [Test]
    public async Task SeventeenPinnedVictimsBoundOneAdmissionToSixteenAttempts()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        using var store = fixture.OpenStore(maxEntries: 17, maxPinsPerEntry: 2);
        fixture.DetachStore(store);
        var keys = Enumerable.Range(0, 18).Select(index => Key("bounded/" + index)).ToArray();
        for (var index = 0; index < 18; index++)
        {
            Put(store, keys[index], [(byte)index]);
        }

        for (var index = 0; index < 17; index++)
        {
            _ = store.Read(view => view.ReadOwnedValue(keys[index]));
        }

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;
        var readers = Enumerable.Range(0, 17)
            .Select(index => Task.Factory.StartNew(() => Borrow(store, keys[index], (byte)index,
                () =>
                {
                    if (Interlocked.Increment(ref enteredCount) == 17)
                    {
                        entered.TrySetResult();
                    }
                }, release), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            await entered.Task.WaitAsync(WaitLimit, TimeProvider.System);
            var before = store.GetPointCacheDiagnostics();
            var fallback = store.Read(view => view.ReadOwnedValue(keys[17]));
            var pressured = store.GetPointCacheDiagnostics();
            await Assert.That(fallback).IsEquivalentTo(new byte[] { 17 });
            await Assert.That(pressured.EvictionAttempts - before.EvictionAttempts).IsEqualTo(16L);
            await Assert.That(pressured.AdmissionBypasses - before.AdmissionBypasses).IsEqualTo(1L);
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(readers);
            foreach (var reader in readers)
            {
                await Assert.That(await reader).IsTrue();
            }
        }
    }

    private static bool Borrow(ZoneTreeStore store, byte[] key, byte expected,
        Action signalEntered, TaskCompletionSource release)
        => store.Read(view => view.ReadValue(key, value =>
        {
            signalEntered();
            release.Task.WaitAsync(WaitLimit, TimeProvider.System).GetAwaiter().GetResult();

            if (value.Length != 1 || value[0] != expected)
            {
                throw new InvalidOperationException("A pinned eviction candidate changed its borrowed value.");
            }
        }));

    private static void Put(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((tx, _) => { tx.Put(key, value); return true; });

    private static byte[] Key(string value) => System.Text.Encoding.UTF8.GetBytes(value);
}
