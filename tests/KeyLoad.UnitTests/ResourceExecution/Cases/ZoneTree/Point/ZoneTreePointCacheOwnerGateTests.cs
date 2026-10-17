using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class ZoneTreePointCacheOwnerGateTests
{
    private static readonly byte[] Key = "cache/owner/gates"u8.ToArray();
    private static readonly byte[] Value = [5, 15, 25];

    [Test]
    public async Task ForeignWriterIsBusyWhileHeldAndForeignReaderCanObserveHealthy()
    {
        using var fixture = new ZoneTreeCoordinatedPointCacheFileFixture();
        await fixture.RunAsync(async () =>
        {
            using var permit = ZoneTreeCoordinatedPointCacheTestSupport.CreateAcceptedPermit(1, out _, out _);
            var store = fixture.OpenStore();
            ZoneTreeCoordinatedPointCacheTestSupport.Put(store, Key, Value);
            var control = ZoneTreeCoordinatedPointCacheTestSupport.CreateControl(store, fixture, permit);
            var expected = new ZoneTreePointCacheOwnerIdentity(store.Identity.NodeId, store.Identity.Incarnation,
                control.RuntimeId);

            await VerifyForeignWriterAsync(store, control);
            await VerifyForeignReaderAsync(store, control, expected);
        });
    }

    private static async Task VerifyForeignWriterAsync(ZoneTreeStore store, ZoneTreePointCacheControl control)
    {
        var failures = new List<Exception>();
        ZoneTreePointCacheOwnerGateHold? hold = null;
        ZoneTreePointCacheOwnerProbe? probe = null;
        try
        {
            await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(async () =>
            {
                hold = new ZoneTreePointCacheOwnerGateHold(store, write: true);
                await hold.WaitUntilEnteredAsync();
                probe = new ZoneTreePointCacheOwnerProbe();
                probe.Start(control);
                var observation = await probe.WaitAsync();
                await Assert.That(observation.Status).IsEqualTo(ZoneTreePointCacheOwnerStatus.Busy);
                await Assert.That(observation.Identity).IsEqualTo(default(ZoneTreePointCacheOwnerIdentity));
                probe = null;
            }, failures);
        }
        finally
        {
            hold?.Release();
            if (probe is not null)
            {
                await probe.JoinAsync(failures);
            }
            if (hold is not null)
            {
                await hold.JoinHolderAsync(failures);
            }
        }

        ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
    }

    private static async Task VerifyForeignReaderAsync(ZoneTreeStore store, ZoneTreePointCacheControl control,
        ZoneTreePointCacheOwnerIdentity expected)
    {
        var failures = new List<Exception>();
        ZoneTreePointCacheOwnerGateHold? hold = null;
        ZoneTreePointCacheOwnerProbe? probe = null;
        try
        {
            await ZoneTreeCoordinatedPointCacheTestSupport.CollectFailureAsync(async () =>
            {
                hold = new ZoneTreePointCacheOwnerGateHold(store, write: false);
                await hold.WaitUntilEnteredAsync();
                probe = new ZoneTreePointCacheOwnerProbe();
                probe.Start(control);
                var observation = await probe.WaitAsync();
                await Assert.That(observation.Status).IsEqualTo(ZoneTreePointCacheOwnerStatus.Healthy);
                await Assert.That(observation.Identity).IsEqualTo(expected);
                probe = null;
            }, failures);
        }
        finally
        {
            hold?.Release();
            if (probe is not null)
            {
                await probe.JoinAsync(failures);
            }
            if (hold is not null)
            {
                await hold.JoinHolderAsync(failures);
            }
        }

        ZoneTreeCoordinatedPointCacheTestSupport.ThrowFailures(failures);
    }
}
