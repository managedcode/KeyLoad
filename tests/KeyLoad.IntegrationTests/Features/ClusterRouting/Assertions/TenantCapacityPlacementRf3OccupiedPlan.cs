using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedPlan
{
    private const long RepairedDirectoryRevision = 5;
    private const long OriginalRowRevision = 1;
    private const int CurrentVersion = 1;
    private const int RequiredMoveCount = 1;
    private const int NativeVoterCount = 6;
    internal static async Task<TenantPlacementPlanResultV1> ReadAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, CancellationToken token)
    {
        var request = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], capacity.Control);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(request, token));
        var mcp = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new { request }, token))).Value;
        await RequireAsync(seed, capacity, sdk);
        await RequireAsync(seed, capacity, mcp);
        return sdk;
    }

    private static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantPlacementPlanResultV1 result)
    {
        await Assert.That(result.Plan.Version).IsEqualTo(CurrentVersion);
        await Assert.That(result.Plan.TenantId).IsEqualTo(seed.Partition.TenantId);
        await Assert.That(result.Plan.DatabaseId).IsEqualTo(seed.Partition.DatabaseId);
        await SqlRf3Protocol.EqualAsync(global::System.Collections.Immutable.ImmutableArray.Create(seed.Partition), result.Plan.OriginalPartitions);
        await Assert.That(result.Plan.PolicyRevision).IsEqualTo(capacity.HealthyPolicy.Revision);
        await Assert.That(result.Plan.ControlPartition).IsEqualTo(capacity.Control);
        await Assert.That(result.Plan.DirectoryRevision).IsEqualTo(RepairedDirectoryRevision);
        await Assert.That(result.Plan.CapacityDigests.Length).IsEqualTo(NativeVoterCount);
        await Assert.That(result.Plan.Moves.Length).IsEqualTo(RequiredMoveCount);
        var move = result.Plan.Moves.Single();
        await Assert.That(move.MoveId).IsNotEqualTo(Guid.Empty);
        await Assert.That(move.Partition).IsEqualTo(seed.Partition);
        await Assert.That(move.ExpectedPlacementRevision).IsEqualTo(OriginalRowRevision);
        await Assert.That(move.DestinationPhysicalShardId).IsEqualTo(seed.FirstRequest.DestinationPhysicalShardId);
        await Assert.That(move.Mode).IsEqualTo(PartitionMoveMode.Transfer);
        await Assert.That(result.Plan.ExpiresAt).IsGreaterThan(result.Plan.SampledAt);
    }
}
