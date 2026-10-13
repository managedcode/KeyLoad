using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3RevocationScenario
{
    internal static async Task RunAsync(CancellationToken token)
    {
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TenantCapacityPlacementRf3Enrollment.StartAsync(token);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, token);
            var state = await TenantCapacityPlacementRf3Provisioning.CreateAsync(seed, token);
            await TenantCapacityPlacementRf3Flow.RunAsync(wave, seed, state,
                committedOwnerLoss: false, originalPlanExpiry: false, pendingIntentRevocation: true, token);
        }, failures);
        if (seed is not null)
        { await ServerFailureObserver.ObserveAsync(() => seed.DisposeAsync().AsTask(), failures); }
        if (wave is not null)
        {
            if (failures.Count != 0)
            { wave.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => wave.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
