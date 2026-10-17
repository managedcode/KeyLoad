using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyRevisionAssertions
{
    private const string McpStaleDetail = "The database operation could not be completed.";
    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        TenantCapacityPlacementRf3PolicyRevision.Refused original, CancellationToken token)
    {
        var sdk = await seed.Source.PersistPlacementIntentAsync(original.Id, original.Request, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.Conflict);
        await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Conflict,
            TenantPlacementPolicyProtocol.Stale), sdk.Problem!);
        var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.PersistTool,
            new
            {
                commandId = original.Id,
                request = original.Request
            }, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Conflict, dispatched: true);
        await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpStaleDetail);
    }
}
