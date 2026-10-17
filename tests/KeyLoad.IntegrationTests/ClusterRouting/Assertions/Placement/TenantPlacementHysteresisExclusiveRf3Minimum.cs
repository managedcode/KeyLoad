using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3Minimum
{
    private const long InitialDirectory = 6;
    private const long OccupiedDirectory = 7;
    private const string ForeignIncarnation = "The document session token belongs to another incarnation.";
    private const string ForeignPlacement = "The document session token belongs to another atomic partition or placement.";

    internal static async Task RequireAsync(TenantPlacementHysteresisExclusiveRf3State state,
        RequestCqrsRf3Callers caller, CancellationToken token)
    {
        if (state.DirectoryRevision == InitialDirectory)
        {
            return;
        }
        var detail = state.DirectoryRevision == OccupiedDirectory ? ForeignIncarnation : ForeignPlacement;
        var reference = new EntityRef(state.Packed.Sibling.Partition, RequestCqrsRf3Protocol.AdminCollection,
            TenantCapacityPlacementRf3OccupiedHealthy.DocumentId);
        var minimum = state.Healthy.SiblingReceipt.Token;
        var sdk = await caller.Sdk.GetAsync(reference, minimum, token);
        await TenantCapacityPlacementRf3Assertions.RefusalAsync(sdk, ErrorCode.TokenInvalidated);
        await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(sdk.Problem, ErrorCode.TokenInvalidated, detail);
        await TenantPlacementHysteresisExclusiveRf3Documents.FreshSiblingAsync(state, caller, token);
        await PartitionMovementPublicRefusalRf3Oracle.McpAsync(await caller.Mcp.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(reference, minimum), token), ErrorCode.TokenInvalidated, detail);
        await TenantPlacementHysteresisExclusiveRf3Documents.FreshSiblingAsync(state, caller, token);
    }
}
