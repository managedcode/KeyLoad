namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardNonRetiredPlanRf3Resume
{
    private const int FirstEntry = 0;
    private const long RevisionStep = 1;
    internal static async Task RunAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, PartitionMoveRequest[] requests, CancellationToken token)
    {
        await wave.RestartJoinedAsync(token);
        await wave.WaitForSixHealthyAsync(token);
        foreach (var seed in seeds)
        {
            await seed.ReconnectAsync(wave, token);
        }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        var settled = await PhysicalShardPartitionMovementRf3Flow.ExecuteAsync(seeds, requests, token);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[index], requests[index],
                seeds[index].OriginalPlacement, seeds[index].Directory.ControlOwner, settled[index],
                checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + index + RevisionStep),
                checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + requests.Length), token);
        }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        await PhysicalShardPartitionMovementRf3Cold.ReplayAsync(wave, seeds, requests, settled, token);
        await PhysicalShardNonRetiredPlanRf3TargetHealthy.RunAsync(wave, seeds, requests, settled, token);
        await PhysicalShardPartitionMovementRf3Flow.MergeAsync(wave, seeds, token);
        foreach (var seed in seeds)
        {
            await PhysicalShardNonRetiredPlanRf3TargetHealthy.RequireCurrentAsync(seed, token);
        }
    }
}
