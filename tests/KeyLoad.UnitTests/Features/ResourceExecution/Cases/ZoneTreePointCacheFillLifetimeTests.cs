using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheFillLifetimeTests
{
    private static readonly byte[] KeyA = "cache/lifetime/a"u8.ToArray();
    private static readonly byte[] KeyB = "cache/lifetime/b"u8.ToArray();
    private static readonly byte[] ValueA = [1, 3, 5];
    private static readonly byte[] ValueB = [2, 4, 6];
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [Test]
    public async Task DuplicateConcurrentFillsKeepBothCandidatesChargedAndReleaseTheLoser()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        using var store = fixture.OpenStore();
        fixture.DetachStore(store);
        Put(store, KeyA, ValueA);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;
        void SignalEntered()
        {
            if (Interlocked.Increment(ref enteredCount) == 2)
            {
                entered.TrySetResult();
            }
        }

        var first = StartFill(() => Fill(store, SignalEntered, release));
        var second = StartFill(() => Fill(store, SignalEntered, release));

        try
        {
            await entered.Task.WaitAsync(WaitLimit, TimeProvider.System);
            var inFlight = await Task.Run(store.GetPointCacheDiagnostics).WaitAsync(WaitLimit, TimeProvider.System);
            var pool = await Task.Run(fixture.Budget.GetSnapshot).WaitAsync(WaitLimit, TimeProvider.System);
            await Assert.That(inFlight.InFlightEntries).IsEqualTo(2);
            await Assert.That(inFlight.ChargedEntries).IsEqualTo(2);
            await Assert.That(inFlight.ActivePins).IsEqualTo(0);
            await Assert.That(pool.RetainedEntries).IsEqualTo(2);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                await first;
            }
            finally
            {
                await second;
            }
        }

        var completed = store.GetPointCacheDiagnostics();
        await Assert.That(completed.InFlightEntries).IsEqualTo(0);
        await Assert.That(completed.LiveEntries).IsEqualTo(1);
        await Assert.That(completed.ChargedEntries).IsEqualTo(1);
        await Assert.That(completed.Admissions).IsEqualTo(1L);
        await Assert.That(completed.NativeLookups).IsEqualTo(2L);
    }

    [Test]
    public async Task PinnedLruVictimStaysChargedAndNewValueFallsBackToNative()
    {
        using var fixture = new ZoneTreePointCacheFileFixture();
        using var store = fixture.OpenStore(maxEntries: 1);
        fixture.DetachStore(store);
        Put(store, KeyA, ValueA);
        Put(store, KeyB, ValueB);
        _ = store.Read(view => view.ReadOwnedValue(KeyA));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        byte[]? borrowed = null;
        var borrower = Task.Run(() => store.Read(view => view.ReadValue(KeyA, value =>
        {
            entered.TrySetResult();
            release.Task.WaitAsync(WaitLimit, TimeProvider.System).GetAwaiter().GetResult();

            borrowed = value.ToArray();
        })));

        try
        {
            await entered.Task.WaitAsync(WaitLimit, TimeProvider.System);
            var before = await Task.Run(store.GetPointCacheDiagnostics).WaitAsync(WaitLimit, TimeProvider.System);
            var fallback = store.Read(view => view.ReadOwnedValue(KeyB));
            var pressure = await Task.Run(store.GetPointCacheDiagnostics).WaitAsync(WaitLimit, TimeProvider.System);
            await Assert.That(fallback).IsEquivalentTo(ValueB);
            await Assert.That(pressure.RetiredPinnedEntries).IsEqualTo(1);
            await Assert.That(pressure.AdmissionBypasses).IsEqualTo(before.AdmissionBypasses + 1);
            await Assert.That(pressure.RetainedBytes).IsEqualTo(before.RetainedBytes);
        }
        finally
        {
            release.TrySetResult();
            await borrower.WaitAsync(WaitLimit, TimeProvider.System);
        }

        var afterRelease = store.GetPointCacheDiagnostics();
        await Assert.That(afterRelease.RetainedBytes).IsEqualTo(1024L + 64L);
        await Assert.That(borrowed).IsEquivalentTo(ValueA);
    }

    private static Task<bool> StartFill(Func<bool> fill)
        => Task.Factory.StartNew(fill, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static bool Fill(ZoneTreeStore store, Action signalEntered, TaskCompletionSource release)
        => store.Read(view => view.ReadValue(KeyA, value =>
        {
            if (!value.SequenceEqual(ValueA))
            {
                throw new InvalidOperationException("The duplicate fill returned unexpected bytes.");
            }
        }, _ =>
        {
            signalEntered();
            release.Task.WaitAsync(WaitLimit, TimeProvider.System).GetAwaiter().GetResult();
        }));

    private static void Put(ZoneTreeStore store, byte[] key, byte[] value)
        => store.Commit((tx, _) => { tx.Put(key, value); return true; });
}
