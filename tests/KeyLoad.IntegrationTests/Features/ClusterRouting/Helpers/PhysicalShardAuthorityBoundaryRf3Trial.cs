using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3Trial
{
    private const int FirstSeed = 0;
    private const int SeedCount = 3;
    internal static async Task RunAsync(CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(RequestCqrsRf3Protocol.ParentDeadline, TimeProvider.System);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        var failures = new List<Exception>();
        TwoRf3MembershipWave? wave = null;
        PhysicalShardAuthorityBoundaryRf3Actor? movement = null;
        HttpClient? sourceHttp = null;
        HttpClient? targetHttp = null;
        var seeds = new List<PartitionMovementPublicParentRf3Seed>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            wave = await TwoRf3MembershipWave.StartProtectedDocumentsAsync(caller.Token);
            seeds.Add(await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, caller.Token));
            while (seeds.Count < SeedCount)
            {
                seeds.Add(await PartitionMovementPublicParentRf3Seed.CreateSiblingAsync(wave, seeds[FirstSeed], caller.Token));
            }
            sourceHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
            targetHttp = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node4);
            var source = new KeyLoadClient(sourceHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
            var target = new KeyLoadClient(targetHttp, wave.Profile.AdminKey, IntegrationClientOptions.Execution());
            movement = await PhysicalShardAuthorityBoundaryRf3Actor.CreateAsync(wave, source, target, caller.Token);
            await PhysicalShardAuthorityBoundaryRf3Flow.RunAsync(wave, seeds.ToArray(), movement, source, target, caller.Token);
        }, failures);
        if (failures.Count > PhysicalShardAuthorityBoundaryRf3Flow.NoFailures)
        {
            wave?.RetainRoots();
        }
        if (movement is { } actor)
        {
            await ServerFailureObserver.ObserveAsync(() => actor.DisposeAsync().AsTask(), failures);
        }
        foreach (var seed in seeds)
        {
            await ServerFailureObserver.ObserveAsync(() => seed.DisposeAsync().AsTask(), failures);
        }
        if (sourceHttp is { } sourceOwned)
        {
            ServerFailureObserver.Observe(sourceOwned.Dispose, failures);
        }
        if (targetHttp is { } targetOwned)
        {
            ServerFailureObserver.Observe(targetOwned.Dispose, failures);
        }
        if (wave is { } application)
        {
            await ServerFailureObserver.ObserveAsync(() => application.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
