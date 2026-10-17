using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreeCoordinatedPointCacheLifecycleTests
{
    private const string RaceFailure = "The factory race produced no accepted outcome.";
    private static readonly byte[] Key = "cache/coordinated/close"u8.ToArray();
    private static readonly byte[] Value = [2, 8, 16];
    private readonly record struct FactoryRaceResult(ZoneTreePointCacheControlResult Result,
        ZoneTreePointCacheControl? Control);

    [Test]
    public async Task ExplicitCloseIsTerminalAndLeavesTheNativeReadPathCorrect()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var receipt);
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
            await Assert.That(control.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
            _ = store.Read(view => view.ReadOwnedValue(Key));
            var beforeClose = control.GetDiagnostics();

            control.CloseAdmission();
            var closed = control.GetDiagnostics();
            var rejected = control.TryApply(receipt);
            var native = store.Read(view => view.ReadOwnedValue(Key));
            var afterNative = control.GetDiagnostics();

            await Assert.That(beforeClose.LiveEntries).IsEqualTo(1);
            await Assert.That(closed.Closed).IsTrue();
            await Assert.That(closed.Enabled).IsFalse();
            await Assert.That(rejected).IsEqualTo(ZoneTreePointCacheControlResult.Closed);
            await Assert.That(native).IsNotNull();
            await Assert.That(native!.SequenceEqual(Value)).IsTrue();
            await Assert.That(afterNative.NativeLookups).IsEqualTo(beforeClose.NativeLookups + 1);
            await Assert.That(afterNative.Hits).IsEqualTo(beforeClose.Hits);
        });
    }

    [Test]
    public async Task ConcurrentRealFactoryAndDisposeLeaveOnlyClosedOrUnconfiguredState()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
            var store = fixture.OpenStore();
            var result = await RaceFactoryAndDisposeAsync(store, fixture, permit);

            await Assert.That(result.Result is ZoneTreePointCacheControlResult.Created
                or ZoneTreePointCacheControlResult.Closed or ZoneTreePointCacheControlResult.Busy).IsTrue();
            await Assert.That((result.Result == ZoneTreePointCacheControlResult.Created) == (result.Control is not null)).IsTrue();
            if (result.Control is not null)
            {
                await Assert.That(result.Control.GetDiagnostics().Closed).IsTrue();
            }
            await Assert.That(fixture.Budget.GetSnapshot().RetainedBytes).IsEqualTo(0L);
        });
    }

    private static async Task<FactoryRaceResult> RaceFactoryAndDisposeAsync(ZoneTreeStore store,
        ZoneTreeCoordinatedPointCacheFileFixture fixture, CacheReadPermit permit)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;
        var failures = new List<Exception>();
        FactoryRaceResult? result = null;
        Task<(ZoneTreePointCacheControlResult Result, ZoneTreePointCacheControl? Control)>? factory = null;
        Task<bool>? dispose = null;
        void SignalReady()
        {
            if (Interlocked.Increment(ref readyCount) == 2)
            {
                ready.TrySetResult();
            }
        }

        try
        {
            await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(async () =>
            {
                factory = StartFactory(store, fixture, permit, start, SignalReady);
                dispose = StartDispose(store, start, SignalReady);
                await ready.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System);
                start.TrySetResult();
                var created = await factory.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System);
                await dispose.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System);
                result = new FactoryRaceResult(created.Result, created.Control);
            }, failures);
        }
        finally
        {
            start.TrySetResult();
            if (factory is not null)
            {
                await ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(factory, failures);
            }
            if (dispose is not null)
            {
                await ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(dispose, failures);
            }
        }

        ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
        return result ?? throw new InvalidOperationException(RaceFailure);
    }

    private static Task<(ZoneTreePointCacheControlResult Result, ZoneTreePointCacheControl? Control)> StartFactory(
        ZoneTreeStore store, ZoneTreeCoordinatedPointCacheFileFixture fixture, CacheReadPermit permit,
        TaskCompletionSource start, Action signalReady)
        => ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() =>
        {
            signalReady();
            start.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System).GetAwaiter().GetResult();
            var result = store.TryCreateCoordinatedPointCache(fixture.CreateOptions(), permit, out var control);
            return (result, control);
        });

    private static Task<bool> StartDispose(ZoneTreeStore store, TaskCompletionSource start, Action signalReady)
        => ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() =>
        {
            signalReady();
            start.Task.WaitAsync(ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit, TimeProvider.System).GetAwaiter().GetResult();
            store.Dispose();
            return true;
        });

}
