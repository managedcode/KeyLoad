using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TenantCapacityPlacementRf3MultiContinuation
{
    private const string DocumentId = "capacity-multi-healthy-continuation";
    private const long DocumentRevision = 1;
    private const string Json = "{\"capacity\":\"healthy\"}";

    internal static async Task RequireAsync(PartitionMovementPublicParentRf3Seed seed, CancellationToken token)
    {
        var command = seed.Models.Command(new PutDocument(RelationalSqlRf3Tokens.Documents, DocumentId, Json));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync(receipt, (await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await seed.Official.CallAsync(McpCallerTools.DocumentsCommit, command, token))).Value);
        var reference = new EntityRef(seed.Partition, RelationalSqlRf3Tokens.Documents, DocumentId);
        await SqlRf3Protocol.EqualAsync(new DocumentResult(reference, DocumentRevision, Json, false, []),
            await PublicReadAssertions.DocumentAsync(await seed.Source.GetAsync(reference, token)));
        await seed.VerifyAsync(token);
    }
}
