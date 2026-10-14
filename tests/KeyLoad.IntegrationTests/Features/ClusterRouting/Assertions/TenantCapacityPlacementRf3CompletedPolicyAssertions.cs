using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3CompletedPolicyAssertions
{
    private const string McpUnavailable = "The operation exceeds the available resource capacity.";
    internal static async Task ConfigureAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        Guid id, ConfigurePlacementPolicyRequest request, TenantPlacementPolicyV1 expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ConfigurePlacementPolicyAsync(id, request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.ConfigureTool,
                new
                {
                    commandId = id,
                    request
                }, token))).Value);
    }

    internal static async Task ReplayAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        TenantCapacityPlacementRf3CompletedPolicyReplay.Identity original, CancellationToken token)
    {
        await TenantCapacityPlacementRf3RevocationAssertions.ReplayAsync(caller,
            original.Completed, original.ApplyId, original.Apply, token);
        await SqlRf3Protocol.EqualAsync(original.Pending, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.PersistPlacementIntentAsync(original.PersistId, original.Persist, token)));
        await SqlRf3Protocol.EqualAsync(original.Pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = original.PersistId,
                    request = original.Persist
                }, token))).Value);
        await PartitionMovementPublicCallerReplay.RequireAsync(caller.Sdk, caller.Official,
            original.Completed.Plan.Moves.Single(), original.Completed.Receipts.Single(), token);
    }

    internal static async Task DisabledPlanAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        TenantPlacementPlanRequest request, CancellationToken token)
    {
        var sdk = await caller.Sdk.PlanTenantPlacementAsync(request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.ResourceExhausted);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.ResourceExhausted,
            TenantPlacementPolicyProtocol.ExpiredPlan), sdk.Problem!);
        var official = await caller.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
        {
            request
        }, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(McpUnavailable);
    }

    internal static async Task FreshPlanAsync(TwoRf3MembershipWave wave,
        TenantCapacityPlacementRf3RevocationCaller caller, TenantPlacementPlanRequest request,
        TenantPlacementPolicyV1 policy, CancellationToken token)
    {
        var discovery = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.PlanTenantPlacementAsync(request, token));
        await TenantCapacityPlacementRf3CompletedPolicyPlanAssertions.RequireAsync(request, policy, discovery, sdk);
        var official = (await McpCallerAssertions.SuccessAsync<TenantPlacementPlanResultV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.PlanTool, new
            {
                request
            }, token))).Value;
        await TenantCapacityPlacementRf3CompletedPolicyPlanAssertions.RequireAsync(request, policy, discovery, official);
    }
}
