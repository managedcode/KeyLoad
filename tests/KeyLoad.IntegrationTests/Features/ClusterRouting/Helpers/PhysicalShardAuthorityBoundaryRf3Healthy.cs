using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3Healthy
{
    private const int FirstEntry = 0;
    private const long RevisionStep = 1;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PhysicalShardAuthorityBoundaryRf3Actor actor, PartitionMoveRequest[] original, PartitionMoveResult first, CancellationToken token)
    {
        var terminals = await PhysicalShardAuthorityBoundaryRf3Execution.ExecuteAsync(actor, original, token);
        await SqlRf3Protocol.EqualAsync(first, terminals[FirstEntry]);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[index], original[index],
                seeds[index].OriginalPlacement, seeds[index].Directory.ControlOwner, terminals[index],
                checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + index + RevisionStep),
                checked(seeds[FirstEntry].OriginalPlacement.DirectoryRevision + original.Length), token);
        }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        await PhysicalShardAuthorityBoundaryRf3Cold.ReplayAsync(wave, seeds, actor, original, terminals, token);
        await PhysicalShardAuthorityBoundaryRf3Merge.RunAsync(wave, seeds, actor, token);
    }
}
