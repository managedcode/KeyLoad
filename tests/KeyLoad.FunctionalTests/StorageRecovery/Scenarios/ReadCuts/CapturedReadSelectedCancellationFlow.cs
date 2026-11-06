using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.FunctionalTests.StorageRecovery.CapturedReadScopeScenario;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class CapturedReadSelectedCancellationFlow
{
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
            var currentPosition = await NativeStoreAssertions.AdvancedAsync(fixture.Store, originalPosition);
            frozen = Capture(lease);
            await RequireCutAsync(lease.Cut, identity, originalPosition);
            lease.Dispose();
            lease = null;
            fixture.Reopen();
            currentPosition = await NativeStoreAssertions.ReopenedAsync(fixture.Store, currentPosition);
            await RequireCurrentAsync(fixture.Store, identity, currentPosition, changed: true, healthy: false);
            await RequireFrozenAsync(frozen);
            foreach (var reverse in new[] { false, true })
            {
                await CapturedReadSelectedCancellationAssertions.BeforeAsync(frozen, reverse);
                await CapturedReadSelectedCancellationAssertions.ObserverAsync(frozen, reverse);
                await CapturedReadSelectedCancellationAssertions.VisitorAsync(frozen, reverse, keepGoing: false);
                await CapturedReadSelectedCancellationAssertions.VisitorAsync(frozen, reverse, keepGoing: true);
                await RequireCurrentAsync(fixture.Store, identity, currentPosition, changed: true, healthy: false);
                await RequireFrozenAsync(frozen);
            }
            CommitHealthy(fixture.Store);
            currentPosition = await NativeStoreAssertions.AdvancedAsync(fixture.Store, currentPosition);
            fixture.Reopen();
            currentPosition = await NativeStoreAssertions.ReopenedAsync(fixture.Store, currentPosition);
            await RequireCurrentAsync(fixture.Store, identity, currentPosition, changed: true, healthy: true);
            await RequireFrozenAsync(frozen);
            fixture.Reopen();
            currentPosition = await NativeStoreAssertions.ReopenedAsync(fixture.Store, currentPosition);
            await RequireCurrentAsync(fixture.Store, identity, currentPosition, changed: true, healthy: true);
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
