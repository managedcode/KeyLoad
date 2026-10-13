using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3MultiScenario
{
    internal static async Task RunAsync(CancellationToken token)
    {
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? first = null;
        PartitionMovementPublicParentRf3Seed? second = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TenantCapacityPlacementRf3Enrollment.StartAsync(token);
            first = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, token);
            second = await PartitionMovementPublicParentRf3Seed.CreateSiblingAsync(wave, first, token);
            var state = await TenantCapacityPlacementRf3Provisioning.CreateAsync(first, token);
            await TenantCapacityPlacementRf3MultiFlow.RunAsync(wave, [first, second], state, token);
        }, failures);
        if (second is not null)
        { await ServerFailureObserver.ObserveAsync(() => second.DisposeAsync().AsTask(), failures); }
        if (first is not null)
        { await ServerFailureObserver.ObserveAsync(() => first.DisposeAsync().AsTask(), failures); }
        if (wave is not null)
        {
            if (failures.Count != 0)
            { wave.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => wave.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
