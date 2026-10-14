using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3ConfiguredOwners
{
    private const long RestrictedRevision = 3;
    private const int OnlyConfiguredOwner = 1;
    private const string McpCapacityDetail = "The operation exceeds the available resource capacity.";

    internal static async Task<TenantCapacityPlacementRf3PolicyWeightsAuthority.Accepted> ConfigureAsync(
        PartitionMovementPublicParentRf3Seed seed, TenantCapacityPlacementRf3Provisioning.State state,
        CancellationToken token)
    {
        var restricted = state.HealthyPolicy with
        {
            Revision = RestrictedRevision,
            OwnerWeights = state.HealthyPolicy.OwnerWeights.Where(owner =>
                owner.PhysicalShardId == seed.Directory.ControlOwner.PhysicalShardId).ToImmutableArray()
        };
        await Assert.That(restricted.OwnerWeights.Length).IsEqualTo(OnlyConfiguredOwner);
        var id = Guid.NewGuid();
        var request = new ConfigurePlacementPolicyRequest(state.HealthyPolicy.Revision, restricted, state.Control);
        await SqlRf3Protocol.EqualAsync(restricted, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ConfigurePlacementPolicyAsync(id, request, token)));
        await CurrentAsync(seed, state, restricted, token);
        await SqlRf3Protocol.EqualAsync(restricted,
            (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(await seed.Official.CallAsync(
                TenantPlacementPublicProtocol.ConfigureTool, new
                {
                    commandId = id,
                    request
                }, token))).Value);
        await CurrentAsync(seed, state, restricted, token);
        return new(id, request);
    }

    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy,
        CancellationToken token)
    {
        var request = new TenantPlacementPlanRequest(seed.Partition.TenantId,
            seed.Partition.DatabaseId, [seed.Partition], state.Control);
        var sdk = await seed.Source.PlanTenantPlacementAsync(request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.ResourceExhausted);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.ResourceExhausted,
            TenantPlacementPolicyProtocol.ScopeMismatch), sdk.Problem!);
        await CurrentAsync(seed, state, policy, token);
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
        {
            request
        }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(string.Equals(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString(), McpCapacityDetail,
            StringComparison.Ordinal)).IsTrue();
        await CurrentAsync(seed, state, policy, token);
    }

    internal static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state,
        TenantCapacityPlacementRf3PolicyWeightsAuthority.Accepted original,
        TenantPlacementPolicyV1 current, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(original.Request.Policy, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ConfigurePlacementPolicyAsync(original.Id, original.Request, token)));
        await CurrentAsync(seed, state, current, token);
        await SqlRf3Protocol.EqualAsync(original.Request.Policy,
            (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(await seed.Official.CallAsync(
                TenantPlacementPublicProtocol.ConfigureTool, new
                {
                    commandId = original.Id,
                    request = original.Request
                }, token))).Value);
        await CurrentAsync(seed, state, current, token);
    }

    private static async Task CurrentAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantPlacementPolicyV1 policy,
        CancellationToken token)
    {
        var request = new TenantPlacementScope(policy.TenantId, policy.DatabaseId, state.Control);
        await SqlRf3Protocol.EqualAsync(policy, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.ReadPlacementPolicyAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(policy,
            (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(await seed.Official.CallAsync(
                TenantPlacementPublicProtocol.PolicyTool, new
                {
                    request
                }, token))).Value);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }
}
