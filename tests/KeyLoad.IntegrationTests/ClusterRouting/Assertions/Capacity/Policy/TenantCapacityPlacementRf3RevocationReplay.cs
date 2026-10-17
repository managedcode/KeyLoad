using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3RevocationReplay
{
    private const long OriginalOutcomeEpoch = 2;
    private const string EpochChanged = "The principal policy changed since this command was evaluated.";
    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3Provisioning.State state, TenantCapacityPlacementRf3RevocationCaller caller,
        PlacementIntentV1 completed, Guid id, ApplyPlacementIntentRequest request,
        PlacementIntentV1 pending, Guid persistId, PersistPlacementIntentRequest persist,
        PlacementIntentV1 oldIntent, Guid oldId, ApplyPlacementIntentRequest oldRequest,
        Guid oldPersistId, PersistPlacementIntentRequest oldPersist, CancellationToken token)
    {
        await TenantCapacityPlacementRf3RevocationAssertions.ReplayAsync(caller, completed, id, request, token);
        await PartitionMovementPublicCallerReplay.RequireAsync(caller.Sdk, caller.Official,
            completed.Plan.Moves.Single(), completed.Receipts.Single(), token);
        await SqlRf3Protocol.EqualAsync(pending,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.PersistPlacementIntentAsync(persistId, persist, token)));
        await SqlRf3Protocol.EqualAsync(pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = persistId,
                    request = persist
                }, token))).Value);
        await Assert.That(oldIntent.PolicyEpoch).IsEqualTo(OriginalOutcomeEpoch);
        await OldPersistDeniedAsync(caller, oldPersistId, oldPersist, token);
        await TenantCapacityPlacementRf3RevocationAssertions.RefuseAsync(caller, oldId, oldRequest,
            ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ScopeMismatch,
            TenantCapacityPlacementRf3RevocationAssertions.McpEpochInvalidated, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(await caller.Sdk.ApplyPlacementIntentAsync(
            id, request with
            {
                ExpectedRevision = completed.Revision
            }, token), ErrorCode.Conflict);
        await TenantCapacityPlacementRf3RevocationAssertions.ReplayAsync(caller, completed, id, request, token);
        await TenantCapacityPlacementRf3Assertions.RequireOriginalModelsAsync(seed, state, token);
    }

    private static async Task OldPersistDeniedAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        Guid id, PersistPlacementIntentRequest request, CancellationToken token)
    {
        var sdk = await caller.Sdk.PersistPlacementIntentAsync(id, request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.PermissionDenied);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, EpochChanged), sdk.Problem!);
        var official = await caller.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
            new
            {
                commandId = id,
                request
            }, token);
        await McpCallerAssertions.ErrorAsync(official, ErrorCode.PermissionDenied, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString())
            .IsEqualTo(TenantCapacityPlacementRf3RevocationAssertions.McpPermissionDenied);
    }
}
