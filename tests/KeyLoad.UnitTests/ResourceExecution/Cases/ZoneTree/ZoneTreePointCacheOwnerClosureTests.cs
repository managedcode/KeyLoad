using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheOwnerClosureTests
{
    private const string PreDrainTimeout = "Pre-drain owner closure was not observable while a real reader remained held.";
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    [Test]
    public async Task ExplicitControlCloseAndCompletedStoreDisposalReturnClosedDefault()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var firstPermit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
            var firstStore = fixture.OpenStore();
            var firstControl = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(firstStore, fixture, firstPermit);
            firstControl.CloseAdmission();
            await AssertClosedAsync(firstControl.TryReadOwnerIdentity(out var firstIdentity), firstIdentity);

            using var secondPermit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
            var secondStore = fixture.OpenStore();
            var secondControl = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(secondStore, fixture, secondPermit);
            secondStore.Dispose();
            await AssertClosedAsync(secondControl.TryReadOwnerIdentity(out var secondIdentity), secondIdentity);
        });
    }

    [Test]
    public async Task StorePreDrainClosureIsVisibleBeforeHeldReaderIsReleased()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(() => VerifyPreDrainClosureAsync(fixture));
    }

    private static async Task VerifyPreDrainClosureAsync(ZoneTreeCoordinatedPointCacheFileFixture fixture)
    {
        using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
        var store = fixture.OpenStore();
        var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
        var failures = new List<Exception>();
        ZoneTreePointCacheOwnerGateHold? hold = null;
        var probe = new ZoneTreePointCacheOwnerProbe();
        Task<bool>? disposal = null;
        try
        {
            await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(async () =>
            {
                hold = new ZoneTreePointCacheOwnerGateHold(store, write: false);
                await hold.WaitUntilEnteredAsync();
                var closing = ZoneTreeCoordinatedPointCacheTestSupport.StartLongRunning(() =>
                {
                    store.Dispose();
                    return true;
                });
                disposal = closing;
                await WaitForClosedAsync(control, probe);
                await Assert.That(closing.IsCompleted).IsFalse();
            }, failures);
        }
        finally
        {
            hold?.Release();
            await probe.JoinAsync(failures);
            if (hold is not null)
            {
                await hold.JoinHolderAsync(failures);
            }
            if (disposal is not null)
            {
                await ZoneTreeCoordinatedPointCacheTestSupport.JoinAndCollectAsync(disposal, failures);
            }
        }

        ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
    }

    private static async Task WaitForClosedAsync(ZoneTreePointCacheControl control,
        ZoneTreePointCacheOwnerProbe probe)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < ZoneTreeCoordinatedPointCacheTestSupport.WaitLimit)
        {
            probe.Start(control);
            var observation = await probe.WaitAsync();
            if (observation.Status == ZoneTreePointCacheOwnerStatus.Closed)
            {
                await AssertClosedAsync(observation.Status, observation.Identity);
                return;
            }

            await Task.Delay(PollInterval, TimeProvider.System);
        }

        throw new TimeoutException(PreDrainTimeout);
    }

    private static async Task AssertClosedAsync(ZoneTreePointCacheOwnerStatus status,
        ZoneTreePointCacheOwnerIdentity identity)
    {
        await Assert.That(status).IsEqualTo(ZoneTreePointCacheOwnerStatus.Closed);
        await Assert.That(identity).IsEqualTo(default(ZoneTreePointCacheOwnerIdentity));
    }
}
