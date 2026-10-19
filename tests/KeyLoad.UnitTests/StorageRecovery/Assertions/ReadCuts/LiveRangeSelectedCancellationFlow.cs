using KeyLoad.Server;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.StorageRecovery.CapturedReadScopeScenario;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class LiveRangeSelectedCancellationFlow
{
    private const long HealthyCommitCount = 1;

    internal static async Task RunAsync()
    {
        ReadCutFixture? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            Seed(fixture.Store);
            var identity = Assertions.ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
            var position = fixture.Store.Position;
            foreach (var staged in new[] { false, true })
            {
                foreach (var reverse in new[] { false, true })
                {
                    await RequireDirectionAsync(fixture, identity, position, staged, reverse);
                }
            }
            CommitHealthy(fixture.Store);
            fixture.Reopen();
            await RequireCurrentAsync(fixture.Store, identity, position + HealthyCommitCount, changed: false, healthy: true);
            fixture.Reopen();
            await RequireCurrentAsync(fixture.Store, identity, position + HealthyCommitCount, changed: false, healthy: true);
        }, failures);
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RequireDirectionAsync(ReadCutFixture fixture, StoreIdentity identity,
        long position, bool staged, bool reverse)
    {
        foreach (var phase in Enum.GetValues<LiveRangeCancellationPhase>())
        {
            if (!staged && phase == LiveRangeCancellationPhase.StagedObserver)
            {
                continue;
            }
            await LiveRangeSelectedCancellationAssertions.RequireAsync(fixture.Store, staged, reverse, phase);
            await RequireCurrentAsync(fixture.Store, identity, position, changed: false, healthy: false);
        }
    }
}
