using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3OccupiedHealthyAssertions
{
    private const long OriginalDocumentRevision = 1;
    internal static async Task RequireAsync(TwoRf3MembershipWave wave, PartitionMovementPublicParentRf3Seed seed,
        TenantDedicatedOwnerRf3State packed, TenantCapacityPlacementRf3OccupiedHealthyState original,
        CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(original.PrimaryReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(original.Primary, token)));
        await SqlRf3Protocol.EqualAsync(original.PrimaryReceipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, original.Primary, token))).Value);
        var reference = new EntityRef(seed.Partition, RelationalSqlRf3Tokens.Documents, TenantCapacityPlacementRf3OccupiedHealthy.DocumentId);
        var expected = new DocumentResult(reference, OriginalDocumentRevision, TenantCapacityPlacementRf3OccupiedHealthy.Json, false, []);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(expected, await PublicReadAssertions.DocumentAsync(
            await sdk.GetAsync(reference, original.PrimaryReceipt.Token, token)));
        }
        await SqlRf3Protocol.EqualAsync(expected, (await PublicReadAssertions.DocumentAsync(await seed.Official.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, original.PrimaryReceipt.Token), token))).Value);
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node1, TwoRf3MembershipProtocol.Node4 })
        {
            _ = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, packed.Sibling.Secret, async caller =>
            {
                await TenantPackingActivationRf3Assertions.ReplayAsync(caller, original.Sibling, original.SiblingReceipt, token);
                var sibling = new EntityRef(packed.Sibling.Partition, RequestCqrsRf3Protocol.AdminCollection,
                    TenantCapacityPlacementRf3OccupiedHealthy.DocumentId);
                var literal = new DocumentResult(sibling, OriginalDocumentRevision, TenantCapacityPlacementRf3OccupiedHealthy.Json, false, []);
                await SqlRf3Protocol.EqualAsync(literal, await PublicReadAssertions.DocumentAsync(
                    await caller.Sdk.GetAsync(sibling, original.SiblingReceipt.Token, token)));
                await SqlRf3Protocol.EqualAsync(literal, (await PublicReadAssertions.DocumentAsync(await caller.Mcp.CallAsync(
                    McpCallerTools.DocumentsGet, new GetDocumentRequest(sibling, original.SiblingReceipt.Token), token))).Value);
                return original.SiblingReceipt;
            }, token);
        }
        await seed.VerifyAsync(token);
    }
}
