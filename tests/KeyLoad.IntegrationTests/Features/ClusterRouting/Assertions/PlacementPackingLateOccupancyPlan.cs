using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PlacementPackingLateOccupancyPlan
{
    private const int WireVersion = 1;
    private const long OriginalDirectoryRevision = 3;
    private const long OriginalSourceRowRevision = 1;
    private const int NativeVoterCount = 6;
    private const int OriginalMoveCount = 1;
    private const int EmptySignedPlanLength = 0;
    internal static async Task<TenantPlacementPlanResultV1> ReadAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, Func<Task> current, CancellationToken token)
    {
        var members = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var cohort = TwoRf3MembershipFingerprintAssertions.Fingerprint(members.Select(value => value.SiloAddress));
        var request = new TenantPlacementPlanRequest(seed.Partition.TenantId, seed.Partition.DatabaseId,
            [seed.Partition], capacity.Control);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.PlanTenantPlacementAsync(request, token));
        await RequireAsync(seed, capacity, sdk, cohort);
        await current();
        var mcp = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
            {
                request
            }, token))).Value;
        await RequireAsync(seed, capacity, mcp, cohort);
        await current();
        return sdk;
    }

    private static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State capacity, TenantPlacementPlanResultV1 actual, string cohort)
    {
        var plan = actual.Plan;
        await Assert.That(plan.Version).IsEqualTo(WireVersion);
        await Assert.That(plan.CohortFingerprint).IsEqualTo(cohort);
        await Assert.That(plan.TenantId).IsEqualTo(seed.Partition.TenantId);
        await Assert.That(plan.DatabaseId).IsEqualTo(seed.Partition.DatabaseId);
        await Assert.That(plan.PolicyRevision).IsEqualTo(capacity.HealthyPolicy.Revision);
        await Assert.That(plan.DirectoryRevision).IsEqualTo(OriginalDirectoryRevision);
        await Assert.That(plan.ControlPartition).IsEqualTo(capacity.Control);
        await SqlRf3Protocol.EqualAsync(global::System.Collections.Immutable.ImmutableArray.Create(seed.Partition), plan.OriginalPartitions);
        await Assert.That(plan.CapacityDigests.Length).IsEqualTo(NativeVoterCount);
        await Assert.That(plan.ExpiresAt).IsGreaterThan(plan.SampledAt);
        await Assert.That(actual.SignedPlan.Length).IsGreaterThan(EmptySignedPlanLength);
        await Assert.That(plan.PlanId).IsNotEqualTo(Guid.Empty);
        await Assert.That(plan.Moves.Length).IsEqualTo(OriginalMoveCount);
        var move = plan.Moves.Single();
        await Assert.That(move.MoveId).IsNotEqualTo(Guid.Empty);
        await Assert.That(move.Partition).IsEqualTo(seed.Partition);
        await Assert.That(move.DestinationPhysicalShardId).IsEqualTo(seed.FirstRequest.DestinationPhysicalShardId);
        await Assert.That(move.ExpectedPlacementRevision).IsEqualTo(OriginalSourceRowRevision);
        await Assert.That(move.Mode).IsEqualTo(PartitionMoveMode.Transfer);
    }
}
