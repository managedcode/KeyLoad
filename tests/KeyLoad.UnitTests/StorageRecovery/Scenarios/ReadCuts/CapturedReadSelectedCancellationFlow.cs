using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.StorageRecovery.CapturedReadScopeScenario;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class CapturedReadSelectedCancellationFlow
{
    private const long ChangedCommitCount = 1;
    private const long HealthyCommitCount = 2;

    internal static async Task RunAsync()
    {
        ReadCutFixture? fixture = null;
        ZoneTreeReadCutLease? lease = null;
        ZoneTreeCapturedReadView? frozen = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new ReadCutFixture();
            Seed(fixture.Store);
            var identity = Assertions.ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
            var originalPosition = fixture.Store.Position;
            lease = fixture.Capture(ReadCutFixture.Limits(MaximumRecords, MaximumBytes));
            Change(fixture.Store);
            frozen = Capture(lease);
            await RequireCutAsync(lease.Cut, identity, originalPosition);
            lease.Dispose();
            lease = null;
            fixture.Reopen();
            await RequireCurrentAsync(fixture.Store, identity, originalPosition + ChangedCommitCount, changed: true, healthy: false);
            await RequireFrozenAsync(frozen);
            foreach (var reverse in new[] { false, true })
            {
                await CapturedReadSelectedCancellationAssertions.BeforeAsync(frozen, reverse);
                await CapturedReadSelectedCancellationAssertions.ObserverAsync(frozen, reverse);
                await CapturedReadSelectedCancellationAssertions.VisitorAsync(frozen, reverse, keepGoing: false);
                await CapturedReadSelectedCancellationAssertions.VisitorAsync(frozen, reverse, keepGoing: true);
                await RequireCurrentAsync(fixture.Store, identity, originalPosition + ChangedCommitCount, changed: true, healthy: false);
                await RequireFrozenAsync(frozen);
            }
            CommitHealthy(fixture.Store);
            fixture.Reopen();
            await RequireCurrentAsync(fixture.Store, identity, originalPosition + HealthyCommitCount, changed: true, healthy: true);
            await RequireFrozenAsync(frozen);
            fixture.Reopen();
            await RequireCurrentAsync(fixture.Store, identity, originalPosition + HealthyCommitCount, changed: true, healthy: true);
            await RequireFrozenAsync(frozen);
        }, failures);
        if (lease is not null)
        {
            ServerFailureObserver.Observe(lease.Dispose, failures);
        }
        if (frozen is not null)
        {
            ServerFailureObserver.Observe(frozen.Dispose, failures);
        }
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
