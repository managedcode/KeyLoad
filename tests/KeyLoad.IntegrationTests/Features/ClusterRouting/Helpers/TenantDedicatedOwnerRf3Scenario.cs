using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantDedicatedOwnerRf3Scenario
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        PartitionMovementPublicParentRf3Seed? seed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(token);
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, token);
            var state = await TenantDedicatedOwnerRf3Provisioning.CreateAsync(wave, seed, token);
            await TenantDedicatedOwnerRf3Flow.RunAsync(wave, seed, state, token);
        }, failures);
        if (seed is { } owned)
        { await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures); }
        if (wave is { } resources)
        {
            if (failures.Count != 0)
            { resources.RetainRoots(); }
            await ServerFailureObserver.ObserveAsync(() => resources.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
