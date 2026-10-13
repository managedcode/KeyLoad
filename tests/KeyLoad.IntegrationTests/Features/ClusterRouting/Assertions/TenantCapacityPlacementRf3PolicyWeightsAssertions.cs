using KeyLoad.Client;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3PolicyWeightsAssertions
{
    private const string McpInvalidDetail = "The database operation could not be completed.";
    internal static async Task RefuseAsync(PartitionMovementPublicParentRf3Seed seed,
        IReadOnlyList<TenantCapacityPlacementRf3PolicyWeightsOperation.Refused> original, CancellationToken token)
    {
        foreach (var item in original)
        {
            var sdk = await seed.Source.ConfigurePlacementPolicyAsync(item.Id, item.Request, token);
            await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.Validation);
            await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.Validation,
                TenantPlacementPolicyProtocol.Invalid), sdk.Problem!);
            var mcp = await seed.Official.CallAsync(TenantPlacementPublicProtocol.ConfigureTool,
                new { commandId = item.Id, request = item.Request }, token);
            await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Validation, dispatched: true);
            await Assert.That(mcp.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpInvalidDetail);
        }
    }
}
