using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardCleanupRestartRf3Trial
{
    private const int FirstSeed = 0;
    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        var seeds = new List<PartitionMovementPublicParentRf3Seed>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token);
            seeds.Add(await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token));
            seeds.Add(await PartitionMovementPublicParentRf3Seed.CreateSiblingAsync(wave, seeds[FirstSeed], caller.Token));
            await Assert.That(seeds[FirstSeed].Partition).IsNotEqualTo(seeds[PhysicalShardCleanupRestartRf3Producer.SecondEntry].Partition);
            await PhysicalShardCleanupRestartRf3Producer.RunAsync(wave, seeds.ToArray(), caller.Token);
        }, failures);
        if (failures.Count > PhysicalShardCleanupRestartRf3Producer.EmptyFailures)
        {
            wave?.RetainRoots();
        }
        foreach (var seed in seeds)
        {
            await ServerFailureObserver.ObserveAsync(() => seed.DisposeAsync().AsTask(), failures);
        }
        if (wave is { } owners)
        {
            await ServerFailureObserver.ObserveAsync(() => owners.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
