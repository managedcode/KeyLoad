using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardPartitionMovementRf3FaultScenario
{
    private const int FirstSeed = 0;
    private const int ExactSeeds = 2;

    internal static Task RunAsync(CancellationToken token)
        => RunAsync(global::KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentPhaseRole.Prepare, token);

    internal static async Task RunAsync(
        global::KeyLoad.Server.Features.ClusterRouting.PartitionMovementParentPhaseRole role, CancellationToken token,
        Func<TwoRf3MembershipWave, PartitionMovementPublicParentRf3Seed[], PartitionMoveRequest[],
            global::KeyLoad.Server.Features.ClusterRouting.RequestCqrsMovementJournalRecord, CancellationToken, Task>? recovery = null)
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
            while (seeds.Count < ExactSeeds)
            { seeds.Add(await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, seeds[FirstSeed].Credential, caller.Token)); }
            await PhysicalShardPartitionMovementRf3FaultFlow.RunAsync(wave, seeds.ToArray(), role, caller.Token, recovery);
        }, failures);
        if (failures.Count > 0 && wave is { } failedWave)
        { failedWave.QueryControls.RetainEvidence(); }
        foreach (var seed in seeds)
        { await ServerFailureObserver.ObserveAsync(() => seed.DisposeAsync().AsTask(), failures); }
        if (wave is { } owned)
        { await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
