using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3FailureDomainsCandidates
{
    private const string McpResourceDetail = "The operation exceeds the available resource capacity.";

    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state,
        TenantCapacityPlacementRf3FailureDomainsOperation.Original original,
        PlacementIntentV1 expected, CancellationToken token)
    {
        var request = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var sdk = await seed.Source.PlanTenantPlacementAsync(request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.ResourceExhausted);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.ResourceExhausted,
            TenantPlacementPolicyProtocol.ScopeMismatch), sdk.Problem!);
        await SqlRf3Protocol.EqualAsync(expected,
            await TenantCapacityPlacementRf3FailureDomainsAssertions.ReadAsync(seed, original, token));
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool,
            new
            {
                request
            }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpResourceDetail);
        await SqlRf3Protocol.EqualAsync(expected,
            await TenantCapacityPlacementRf3FailureDomainsAssertions.ReadAsync(seed, original, token));
        await TenantCapacityPlacementRf3FailureDomainsAuthority.PersistReplayAsync(seed,
            original.PersistId, original.Persist, original.Pending, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }
}
