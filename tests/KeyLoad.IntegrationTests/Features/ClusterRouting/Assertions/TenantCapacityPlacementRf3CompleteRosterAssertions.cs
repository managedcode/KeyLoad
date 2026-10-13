using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3CompleteRosterAssertions
{
    private const int FirstSeed = 0;
    private const int RequiredSeedCount = 2;
    private const int OmittedSelectionCount = 1;
    private const string McpScopeDetail = "The database operation could not be completed.";
    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        PartitionMovementPublicParentRf3Seed[] seeds, TenantCapacityPlacementRf3Provisioning.State state,
        CancellationToken token)
    {
        await Assert.That(seeds.Length).IsEqualTo(RequiredSeedCount);
        await Assert.That(seeds.Select(seed => seed.Partition).Distinct().Count()).IsEqualTo(RequiredSeedCount);
        var first = seeds[FirstSeed];
        var request = new TenantPlacementPlanRequest(first.Partition.TenantId,
            first.Partition.DatabaseId, [first.Partition], state.Control);
        await Assert.That(request.Partitions.Length).IsEqualTo(OmittedSelectionCount);
        await RefuseAsync(first, request, token);
        foreach (var seed in seeds)
        { await seed.VerifyAsync(token); }
        await TenantCapacityPlacementRf3MultiCold.RunAsync(wave, seeds, state,
            seeds.Select(seed => seed.FirstRequest).ToArray(), null, null, null, token);
        await RefuseAsync(first, request, token);
        foreach (var seed in seeds)
        { await seed.VerifyAsync(token); }
    }

    private static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed first,
        TenantPlacementPlanRequest request, CancellationToken token)
    {
        var sdk = await first.Source.PlanTenantPlacementAsync(request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.OwnershipLost);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.OwnershipLost,
            TenantPlacementPolicyProtocol.ScopeMismatch), sdk.Problem!);
        var mcp = await first.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new { request }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.OwnershipLost, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpScopeDetail);
    }
}
