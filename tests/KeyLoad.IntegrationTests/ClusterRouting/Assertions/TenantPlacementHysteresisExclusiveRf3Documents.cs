using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantPlacementHysteresisExclusiveRf3Documents
{
    private const long DocumentRevision = 1;

    internal static async Task RequireAsync(TenantPlacementHysteresisExclusiveRf3State state,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(state.Healthy.PrimaryReceipt, await McpCallerAssertions.SdkSuccessAsync(
            await state.Seed.Source.CommitAsync(state.Healthy.Primary, token)));
        await SqlRf3Protocol.EqualAsync(state.Healthy.PrimaryReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await state.Seed.Official.CallAsync(McpCallerTools.DocumentsCommit, state.Healthy.Primary, token))).Value);
        var reference = new EntityRef(state.Seed.Partition, RelationalSqlRf3Tokens.Documents,
            TenantCapacityPlacementRf3OccupiedHealthy.DocumentId);
        var expected = new DocumentResult(reference, DocumentRevision,
            TenantCapacityPlacementRf3OccupiedHealthy.Json, false, []);
        foreach (var sdk in new[] { state.Seed.Source, state.Seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
                await sdk.GetAsync(reference, state.Healthy.PrimaryReceipt.Token, token)));
        }
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(
            await state.Seed.Official.CallAsync(McpCallerTools.DocumentsGet,
                new GetDocumentRequest(reference, state.Healthy.PrimaryReceipt.Token), token))).Value);
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(state.Wave.Application, TwoRf3MembershipProtocol.Node1,
            state.Packed.Sibling.Secret, async caller =>
            {
                await TenantPackingActivationRf3Assertions.ReplayAsync(caller,
                    state.Packed.SiblingSeed, state.Packed.SiblingReceipt, token);
                await TenantPackingActivationRf3Assertions.ReplayAsync(caller,
                    state.Healthy.Sibling, state.Healthy.SiblingReceipt, token);
                await FreshSiblingAsync(state, caller, token);
                await TenantPlacementHysteresisExclusiveRf3Minimum.RequireAsync(state, caller, token);
                return state.Healthy.SiblingReceipt;
            }, token);
    }

    internal static async Task FreshSiblingAsync(TenantPlacementHysteresisExclusiveRf3State state,
        RequestCqrsRf3Callers caller, CancellationToken token)
    {
        await LiteralAsync(caller, new(state.Packed.Sibling.Partition, RequestCqrsRf3Protocol.AdminCollection,
            RequestCqrsRf3Protocol.DocumentId), RequestCqrsRf3Protocol.DocumentJson, token);
        await LiteralAsync(caller, new(state.Packed.Sibling.Partition, RequestCqrsRf3Protocol.AdminCollection,
            TenantCapacityPlacementRf3OccupiedHealthy.DocumentId), TenantCapacityPlacementRf3OccupiedHealthy.Json, token);
    }

    private static async Task LiteralAsync(RequestCqrsRf3Callers caller, EntityRef reference,
        string json, CancellationToken token)
    {
        var expected = new DocumentResult(reference, DocumentRevision, json, false, []);
        await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
            await caller.Sdk.GetAsync(reference, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(await caller.Mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference), token))).Value);
    }
}
