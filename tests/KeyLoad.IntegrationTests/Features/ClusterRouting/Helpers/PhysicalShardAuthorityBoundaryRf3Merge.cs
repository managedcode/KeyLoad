using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PhysicalShardAuthorityBoundaryRf3Merge
{
    private const int FirstEntry = 0;
    private const int PlacementVersion = 1;
    private const long RevisionStep = 1;
    internal static async Task RunAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed[] seeds,
        PhysicalShardAuthorityBoundaryRf3Actor actor, CancellationToken token)
    {
        var requests = new PartitionMoveRequest[seeds.Length];
        var placements = new AtomicPartitionPlacementResolution[seeds.Length];
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            var seed = seeds[index];
            placements[index] = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadAtomicPartitionPlacementAsync(
                new(PlacementVersion, seed.Partition), token));
            requests[index] = new(Guid.NewGuid(), seed.Partition, seed.Directory.ControlOwner.PhysicalShardId,
                placements[index].Revision, PartitionMoveMode.Transfer);
        }
        var merged = await PhysicalShardAuthorityBoundaryRf3Execution.ExecuteAsync(actor, requests, token);
        for (var index = FirstEntry; index < seeds.Length; index++)
        {
            var owner = seeds[index].Directory.Owners.Single(entry => entry.Owner.PhysicalShardId
                == placements[index].PhysicalShardId).Owner;
            await PhysicalShardPartitionMovementRf3Placement.RequireAsync(seeds[index], requests[index],
                placements[index], owner, merged[index],
                checked(placements[FirstEntry].DirectoryRevision + index + RevisionStep),
                checked(placements[FirstEntry].DirectoryRevision + requests.Length), token);
        }
        await PhysicalShardPartitionMovementRf3Flow.VerifyAsync(seeds, token);
        await PhysicalShardAuthorityBoundaryRf3Cold.ReplayAsync(wave, seeds, actor, requests, merged, token);
        await PhysicalShardPartitionMovementRf3Healthy.RequireAsync(wave, seeds, requests, merged, token);
    }
}
