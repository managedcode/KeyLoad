using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheOwnerIdentityTests
{
    private static readonly byte[] Key = "cache/owner/identity"u8.ToArray();
    private static readonly byte[] Value = [13, 29, 47];

    // AC-CACHE-013.1: a cold configured owner exposes only its real local tuple and changes no counters.
    [Test]
    public async Task ColdObservationReturnsOnlyTheActualOwnerWithoutChangingWork()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(() => VerifyColdObservationAsync(fixture));
    }

    // AC-CACHE-013.1/.2: warm observation is read-only and real current-thread callbacks are Busy.
    [Test]
    public async Task WarmObservationAndSameThreadOperationsPreserveTheCanonicalRead()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(() => VerifyWarmAndCallbacksAsync(fixture));
    }

    private static async Task VerifyColdObservationAsync(ZoneTreeCoordinatedPointCacheFileFixture fixture)
    {
        using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
        var store = fixture.OpenStore();
        ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
        var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
        var expected = Owner(store, control);
        var before = CaptureWork(store, control, fixture);

        var status = control.TryReadOwnerIdentity(out var identity);
        await AssertOwnerAsync(status, identity, expected);
        await Assert.That(CaptureWork(store, control, fixture)).IsEqualTo(before);
    }

    private static async Task VerifyWarmAndCallbacksAsync(ZoneTreeCoordinatedPointCacheFileFixture fixture)
    {
        using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out var receipt);
        var store = fixture.OpenStore();
        ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
        var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
        var expected = Owner(store, control);
        await Assert.That(control.TryApply(receipt)).IsEqualTo(ZoneTreePointCacheControlResult.Applied);
        await ReadExpectedAsync(store);
        await ReadExpectedAsync(store);
        var readyCache = control.GetDiagnostics();
        await Assert.That(readyCache.Enabled).IsTrue();
        await Assert.That(readyCache.RetainedBytes).IsGreaterThan(0L);
        await Assert.That(readyCache.LiveEntries).IsEqualTo(1);
        await Assert.That(readyCache.Hits).IsEqualTo(1L);

        var before = CaptureWork(store, control, fixture);
        await AssertOwnerAsync(control.TryReadOwnerIdentity(out var observed), observed, expected);
        await Assert.That(CaptureWork(store, control, fixture)).IsEqualTo(before);
        await AssertCallbacksBusyAsync(store, control);
        await ReadExpectedAsync(store);
        await AssertOwnerAsync(control.TryReadOwnerIdentity(out var healthy), healthy, expected);
    }

    private static async Task AssertCallbacksBusyAsync(ZoneTreeStore store, ZoneTreePointCacheControl control)
    {
        ZoneTreePointCacheOwnerStatus? readStatus = null;
        ZoneTreePointCacheOwnerIdentity readIdentity = default;
        _ = store.Read(_ => { readStatus = control.TryReadOwnerIdentity(out readIdentity); return true; });
        ZoneTreePointCacheOwnerStatus? writeStatus = null;
        ZoneTreePointCacheOwnerIdentity writeIdentity = default;
        _ = store.Commit((_, _) => { writeStatus = control.TryReadOwnerIdentity(out writeIdentity); return true; });
        await Assert.That(readStatus).IsEqualTo(ZoneTreePointCacheOwnerStatus.Busy);
        await Assert.That(readIdentity).IsEqualTo(default(ZoneTreePointCacheOwnerIdentity));
        await Assert.That(writeStatus).IsEqualTo(ZoneTreePointCacheOwnerStatus.Busy);
        await Assert.That(writeIdentity).IsEqualTo(default(ZoneTreePointCacheOwnerIdentity));
    }

    private static async Task ReadExpectedAsync(ZoneTreeStore store)
    {
        var actual = store.Read(view => view.ReadOwnedValue(Key));
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual!.AsSpan().SequenceEqual(Value)).IsTrue();
    }

    private static ZoneTreePointCacheOwnerIdentity Owner(ZoneTreeStore store, ZoneTreePointCacheControl control)
        => new(store.Identity.NodeId, store.Identity.Incarnation, control.RuntimeId);

    [Test]
    public async Task ReopeningTheSameDirectoryKeepsPersistedIdsAndCreatesANewRuntimeId()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            var firstStore = fixture.OpenStore();
            using var firstPermit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
            var firstControl = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(firstStore, fixture, firstPermit);
            var first = new ZoneTreePointCacheOwnerIdentity(firstStore.Identity.NodeId, firstStore.Identity.Incarnation,
                firstControl.RuntimeId);

            firstStore.Dispose();
            var reopened = fixture.ReopenStoreAtSameDirectory(firstStore);
            using var secondPermit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
            var secondControl = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(reopened, fixture, secondPermit);
            var second = new ZoneTreePointCacheOwnerIdentity(reopened.Identity.NodeId, reopened.Identity.Incarnation,
                secondControl.RuntimeId);

            await AssertOwnerAsync(secondControl.TryReadOwnerIdentity(out var observed), observed, second);
            await Assert.That(second.NodeId).IsEqualTo(first.NodeId);
            await Assert.That(second.Incarnation).IsEqualTo(first.Incarnation);
            await Assert.That(second.RuntimeId).IsNotEqualTo(first.RuntimeId);
        });
    }

    private static (ZoneTreePointCacheSnapshot Cache, ZoneTreeReadSnapshot Reads, CacheMemorySnapshot Budget)
        CaptureWork(ZoneTreeStore store, ZoneTreePointCacheControl control,
            ZoneTreeCoordinatedPointCacheFileFixture fixture)
        => (control.GetDiagnostics(), store.GetReadDiagnostics(), fixture.Budget.GetSnapshot());

    private static async Task AssertOwnerAsync(ZoneTreePointCacheOwnerStatus status,
        ZoneTreePointCacheOwnerIdentity identity, ZoneTreePointCacheOwnerIdentity expected)
    {
        await Assert.That(status).IsEqualTo(ZoneTreePointCacheOwnerStatus.Healthy);
        await Assert.That(identity).IsEqualTo(expected);
    }
}
