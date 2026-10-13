using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3Scenario
{
    internal static async Task RunAsync(CancellationToken token)
        => await RunAsync(committedOwnerLoss: false, token).ConfigureAwait(false);

    internal static async Task RunAsync(bool committedOwnerLoss, CancellationToken token)
    {
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TenantCapacityPlacementRf3Enrollment.StartAsync(token).ConfigureAwait(false);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, token).ConfigureAwait(false);
            var state = await TenantCapacityPlacementRf3Provisioning.CreateAsync(seed, token).ConfigureAwait(false);
            await TenantCapacityPlacementRf3Flow.RunAsync(wave, seed, state, committedOwnerLoss, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (seed is not null)
        { await ServerFailureObserver.ObserveAsync(() => seed.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        if (wave is not null)
        {
            if (failures.Count != 0)
            { wave.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => wave.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
