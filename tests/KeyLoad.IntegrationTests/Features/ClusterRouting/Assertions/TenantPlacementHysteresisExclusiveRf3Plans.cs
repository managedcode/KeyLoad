using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3Plans
{
    private const long OccupiedDirectory = 7;
    private const string SafeCapacity = "The operation exceeds the available resource capacity.";

    internal static async Task RequireAsync(TenantPlacementHysteresisExclusiveRf3State state,
        CancellationToken token)
    {
        if (state.DirectoryRevision == OccupiedDirectory)
        {
            await RefuseAsync(state, token);
            return;
        }
        var discovery = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(
            state.Wave.Application, state.Wave.Profile, token);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(
            await state.Seed.Source.PlanTenantPlacementAsync(state.PlanRequest, token));
        await TenantPlacementHysteresisExclusiveRf3PlanFields.RequireAsync(state, discovery, sdk);
        await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
        var mcp = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await state.Seed.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool,
                new
                {
                    request = state.PlanRequest
                }, token))).Value;
        await TenantPlacementHysteresisExclusiveRf3PlanFields.RequireAsync(state, discovery, mcp);
        await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
    }

    private static async Task RefuseAsync(TenantPlacementHysteresisExclusiveRf3State state,
        CancellationToken token)
    {
        var sdk = await state.Seed.Source.PlanTenantPlacementAsync(state.PlanRequest, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.ResourceExhausted);
        await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(sdk.Problem,
            ErrorCode.ResourceExhausted, TenantPlacementPolicyProtocol.ScopeMismatch);
        await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await state.Seed.Official.CallAsync(
            TenantPlacementPublicProtocol.PlanTool, new
            {
                request = state.PlanRequest
            }, token),
            ErrorCode.ResourceExhausted, SafeCapacity);
        await TenantPlacementHysteresisExclusiveRf3Current.RequireAsync(state, token);
    }
}
