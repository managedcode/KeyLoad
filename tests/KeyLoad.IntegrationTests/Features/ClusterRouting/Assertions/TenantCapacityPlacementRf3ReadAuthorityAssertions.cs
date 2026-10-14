using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3ReadAuthorityAssertions
{
    internal static async Task DeniedAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        TenantPlacementScope scope, PlacementIntentV1 original, CancellationToken token)
    {
        var policy = await caller.Sdk.ReadPlacementPolicyAsync(scope, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(policy, ErrorCode.PermissionDenied);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
            KeyLoad.Clustering.GrainRoutingProtocol.AdministrationRequired), policy.Problem!);
        var officialPolicy = await caller.Official.CallAsync(TenantPlacementPublicProtocol.PolicyTool,
            new
            {
                request = scope
            }, token);
        await McpCallerAssertions.ErrorAsync(officialPolicy, ErrorCode.PermissionDenied, dispatched: true);
        await Assert.That(officialPolicy.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(TenantCapacityPlacementRf3RevocationAssertions.McpPermissionDenied);
        var request = new PlacementIntentReadRequest(scope, original.IntentId);
        var intent = await caller.Sdk.ReadPlacementIntentAsync(request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(intent, ErrorCode.PermissionDenied);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied,
            KeyLoad.Clustering.GrainRoutingProtocol.AdministrationRequired), intent.Problem!);
        var officialIntent = await caller.Official.CallAsync(TenantPlacementPublicProtocol.IntentTool,
            new
            {
                request
            }, token);
        await McpCallerAssertions.ErrorAsync(officialIntent, ErrorCode.PermissionDenied, dispatched: true);
        await Assert.That(officialIntent.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(TenantCapacityPlacementRf3RevocationAssertions.McpPermissionDenied);
    }

    internal static async Task EpochFenceAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        TenantPlacementScope scope, TenantPlacementPolicyV1 policy, PlacementIntentV1 original, CancellationToken token)
    {
        await PolicyAsync(caller, scope, policy, token);
        var request = new PlacementIntentReadRequest(scope, original.IntentId);
        var sdk = await caller.Sdk.ReadPlacementIntentAsync(request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.TokenInvalidated);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.TokenInvalidated,
            TenantPlacementPolicyProtocol.ScopeMismatch), sdk.Problem!);
        var mcp = await caller.Official.CallAsync(TenantPlacementPublicProtocol.IntentTool, new
        {
            request
        }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.TokenInvalidated, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(TenantCapacityPlacementRf3RevocationAssertions.McpEpochInvalidated);
    }

    internal static async Task CurrentAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        TenantPlacementScope scope, TenantPlacementPolicyV1 policy, PlacementIntentV1 expected, CancellationToken token)
    {
        await PolicyAsync(caller, scope, policy, token);
        var request = new PlacementIntentReadRequest(scope, expected.IntentId);
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadPlacementIntentAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.IntentTool, new
            {
                request
            }, token))).Value);
    }

    private static async Task PolicyAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        TenantPlacementScope request, TenantPlacementPolicyV1 expected, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(expected,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadPlacementPolicyAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<TenantPlacementPolicyV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.PolicyTool, new
            {
                request
            }, token))).Value);
    }
}
