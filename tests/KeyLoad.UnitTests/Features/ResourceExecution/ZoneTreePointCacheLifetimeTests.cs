using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheLifetimeTests
{
    private static readonly byte[] KeyA = "cache/lifetime/a"u8.ToArray();
    private static readonly byte[] ValueA = [1, 3, 5];
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ReaderAndObserverFailuresReleasePinsAndCandidatesBeforeHealthyReads()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore();
        Put(store, KeyA, ValueA);

        Assert.ThrowsExactly<OperationCanceledException>(() => store.Read(view => view.ReadValue(KeyA,
            _ => throw new InvalidOperationException("Reader must not follow canceled observer."),
            _ => throw new OperationCanceledException())));
        var afterObserverFailure = store.GetPointCacheDiagnostics();
        _ = store.Read(view => view.ReadOwnedValue(KeyA));
        Assert.ThrowsExactly<OperationCanceledException>(() => store.Read(view => view.ReadValue(KeyA,
            _ => throw new InvalidOperationException("Reader must not follow canceled observer."),
            _ => throw new OperationCanceledException())));
        var afterWarmObserverFailure = store.GetPointCacheDiagnostics();
        Assert.ThrowsExactly<InvalidOperationException>(() => store.Read(view => view.ReadValue(KeyA,
            _ => throw new InvalidOperationException("Intentional consumer failure."))));
        var afterReaderFailure = store.GetPointCacheDiagnostics();
        var healthy = store.Read(view => view.ReadOwnedValue(KeyA));
        var final = store.GetPointCacheDiagnostics();

        await Assert.That(healthy).IsEquivalentTo(ValueA);
        await Assert.That(afterObserverFailure.ActivePins).IsEqualTo(0);
        await Assert.That(afterObserverFailure.InFlightEntries).IsEqualTo(0);
        await Assert.That(afterObserverFailure.ChargedEntries).IsEqualTo(0);
        await Assert.That(afterWarmObserverFailure.ActivePins).IsEqualTo(0);
        await Assert.That(afterWarmObserverFailure.InFlightEntries).IsEqualTo(0);
        await Assert.That(afterWarmObserverFailure.ChargedEntries).IsEqualTo(1);
        await Assert.That(afterReaderFailure.ActivePins).IsEqualTo(0);
        await Assert.That(afterReaderFailure.InFlightEntries).IsEqualTo(0);
        await Assert.That(final.ActivePins).IsEqualTo(0);
        await Assert.That(final.InFlightEntries).IsEqualTo(0);
        await Assert.That(final.NativeLookups).IsEqualTo(2L);
    }

    [Test]
    public async Task DisableDuringPinnedBorrowRetiresEntryUntilBorrowerReleases()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        var store = fixture.OpenStore(maxPinsPerEntry: 1);
        Put(store, KeyA, ValueA);
        _ = store.Read(view => view.ReadOwnedValue(KeyA));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[]? borrowed = null;
        var borrower = Task.Run(() => store.Read(view => view.ReadValue(KeyA, value =>
        {
            entered.TrySetResult();
            release.Task.WaitAsync(WaitLimit).GetAwaiter().GetResult();

            borrowed = value.ToArray();
        })));
        Task? disabling = null;

        try
        {
            await entered.Task.WaitAsync(WaitLimit);
            var beforeDisable = await Task.Run(store.GetPointCacheDiagnostics).WaitAsync(WaitLimit);
            var nativeFallback = store.Read(view => view.ReadOwnedValue(KeyA));
            var afterPinLimit = await Task.Run(store.GetPointCacheDiagnostics).WaitAsync(WaitLimit);
            await Assert.That(nativeFallback).IsEquivalentTo(ValueA);
            await Assert.That(afterPinLimit.ReadBypasses - beforeDisable.ReadBypasses).IsEqualTo(1L);
            await Assert.That(afterPinLimit.NativeLookups - beforeDisable.NativeLookups).IsEqualTo(1L);
            disabling = Task.Run(store.DisablePointCache);
            await disabling.WaitAsync(WaitLimit);
            var retired = await Task.Run(store.GetPointCacheDiagnostics).WaitAsync(WaitLimit);
            await Assert.That(beforeDisable.ActivePins).IsEqualTo(1);
            await Assert.That(retired.Enabled).IsFalse();
            await Assert.That(retired.RetiredPinnedEntries).IsEqualTo(1);
            await Assert.That(retired.ActivePins).IsEqualTo(1);
            await Assert.That(retired.RetainedBytes).IsEqualTo(beforeDisable.RetainedBytes);
        }
        finally
        {
            release.TrySetResult();
            await borrower.WaitAsync(WaitLimit);
            if (disabling is not null)
            {
                await disabling.WaitAsync(WaitLimit);
            }
        }

        await AssertReleasedBorrowAsync(store, borrowed);
    }

    private static async Task AssertReleasedBorrowAsync(ZoneTreeStore store, byte[]? borrowed)
    {
        var released = store.GetPointCacheDiagnostics();
        await Assert.That(released.ActivePins).IsEqualTo(0);
        await Assert.That(released.RetiredPinnedEntries).IsEqualTo(0);
        await Assert.That(released.RetainedBytes).IsEqualTo(1024L + 64L * 8);
        await Assert.That(borrowed).IsEquivalentTo(ValueA);
    }

    private static void Put(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((tx, _) => { tx.Put(key, value); return true; });
}
