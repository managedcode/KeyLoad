using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3IntentCollisionAssertions
{
    private const string McpStaleDetail = "The database operation could not be completed.";
    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3IntentCollisionState original, CancellationToken token)
    {
        var sdk = await seed.Source.PersistPlacementIntentAsync(original.CollisionId, original.CollisionRequest, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.Conflict);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale), sdk.Problem!);
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
            new
            {
                commandId = original.CollisionId,
                request = original.CollisionRequest
            }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Conflict, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpStaleDetail);
        await PendingAsync(seed, original, token);
    }

    internal static async Task PendingAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3IntentCollisionState original, CancellationToken token)
    {
        var request = new PlacementIntentReadRequest(new(seed.Partition.TenantId,
            seed.Partition.DatabaseId, original.Request.ControlPartition), original.Request.IntentId);
        await SqlRf3Protocol.EqualAsync(original.Pending,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.ReadPlacementIntentAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(original.Pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.IntentTool, new
            {
                request
            }, token))).Value);
        await SqlRf3Protocol.EqualAsync(original.Pending, await McpCallerAssertions.SdkSuccessAsync(
            await seed.Source.PersistPlacementIntentAsync(original.PersistId, original.Request, token)));
        await SqlRf3Protocol.EqualAsync(original.Pending, (await McpCallerAssertions.SuccessAsync<PlacementIntentV1>(
            await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
                new
                {
                    commandId = original.PersistId,
                    request = original.Request
                }, token))).Value);
    }
}
