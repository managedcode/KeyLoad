using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3RevocationAssertions
{
    internal const string McpPermissionDenied = "The authenticated principal is not permitted to perform this operation.";
    internal const string McpEpochInvalidated = "The database operation could not be completed.";
    private const long CompletedRevision = 3;
    private const int CompletedMoveCount = 1;
    internal static async Task RefuseAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        Guid id, ApplyPlacementIntentRequest request, ErrorCode code, string detail, string mcpDetail, CancellationToken token)
    {
        var sdk = await caller.Sdk.ApplyPlacementIntentAsync(id, request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, code);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(code, detail), sdk.Problem!);
        var official = await caller.Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
            new
            {
                commandId = id,
                request
            }, token);
        await McpCallerAssertions.ErrorAsync(official, code, dispatched: true);
        await Assert.That(official.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(mcpDetail);
    }

    internal static async Task CompletedAsync(PlacementIntentV1 pending, PlacementIntentV1 actual,
        Guid id, ApplyPlacementIntentRequest request)
    {
        await Assert.That(actual.Status).IsEqualTo(PlacementIntentStatus.Completed);
        await Assert.That(actual.Revision).IsEqualTo(CompletedRevision);
        await Assert.That(actual.CompletedMoves).IsEqualTo(CompletedMoveCount);
        await Assert.That(actual.Receipts.Length).IsEqualTo(1);
        await Assert.That(actual.Claim).IsNotNull();
        await Assert.That(actual.OriginalApplyCommandId).IsEqualTo(id);
        await Assert.That(actual.OriginalApplyRequest).IsEqualTo(request);
        await Assert.That(actual.PrincipalId).IsEqualTo(pending.PrincipalId);
        await Assert.That(actual.PolicyEpoch).IsEqualTo(pending.PolicyEpoch);
        await SqlRf3Protocol.EqualAsync(pending.Plan, actual.Plan);
        await SqlRf3Protocol.EqualAsync(pending.ControlOwner, actual.ControlOwner);
    }

    internal static async Task ReplayAsync(TenantCapacityPlacementRf3RevocationCaller caller,
        PlacementIntentV1 original, Guid id, ApplyPlacementIntentRequest request, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(original,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ApplyPlacementIntentAsync(id, request, token)));
        await SqlRf3Protocol.EqualAsync(original, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await caller.Official.CallAsync(TenantPlacementPublicProtocol.ApplyTool,
                new
                {
                    commandId = id,
                    request
                }, token))).Value);
        await SqlRf3Protocol.EqualAsync(original,
            await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadPlacementIntentAsync(
                new(new(request.TenantId, request.DatabaseId, request.ControlPartition), original.IntentId), token)));
    }
}
