using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementLifetimeProofRf3Calls
{
    private const long InitialRevision = 0;
    private const string SdkDocument = "lifetime-sdk-donor";
    private const string McpDocument = "lifetime-mcp-probed";
    private const string ColdSdkDocument = "lifetime-sdk-cold";
    private const string ColdMcpDocument = "lifetime-mcp-cold";
    private const string SdkJson = "{\"lifetime\":\"sdk-donor\"}";
    private const string McpJson = "{\"lifetime\":\"mcp-probed\"}";
    private const string ColdSdkJson = "{\"lifetime\":\"sdk-cold\"}";
    private const string ColdMcpJson = "{\"lifetime\":\"mcp-cold\"}";

    internal static (CommandRequest Command, EntityRef Entity, string Json) Create(
        PartitionMovementPublicParentRf3Seed seed, PartitionMoveResult terminal, bool official, bool cold)
    {
        var reference = new EntityRef(seed.Partition, RelationalSqlRf3Tokens.Documents,
            cold ? official ? ColdMcpDocument : ColdSdkDocument : official ? McpDocument : SdkDocument);
        var json = cold ? official ? ColdMcpJson : ColdSdkJson : official ? McpJson : SdkJson;
        var command = new CommandRequest(Guid.NewGuid(), seed.Partition,
            [new PutDocument(reference.Collection, reference.Id, json, InitialRevision)], terminal.PublishedPlacement!.PlacementEpoch);
        return (command, reference, json);
    }

    internal static PartitionMovementLifetimeProofRf3Original Start(PartitionMovementPublicParentRf3Seed seed,
        (CommandRequest Command, EntityRef Entity, string Json) input, bool official, Guid armId, CancellationToken token)
    {
        if (official)
        {
            var actual = seed.Official.CallAsync(McpCallerTools.DocumentsCommit, input.Command, token);
            return new(input.Command, input.Entity, input.Json, armId, actual,
                async () => (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await actual)).Value);
        }
        var original = seed.Source.CommitAsync(input.Command, token);
        return new(input.Command, input.Entity, input.Json, armId, original,
            async () => await McpCallerAssertions.SdkSuccessAsync(await original));
    }

    internal static async Task CompleteAsync(PartitionMovementPublicParentRf3Seed seed, PartitionMoveResult terminal,
        PartitionMovementLifetimeProofRf3Original original, CancellationToken token)
    {
        original.Receipt = await original.Result();
        await ReplayAsync(seed, original, token);
        await PartitionMovementLifetimeProofRf3Assertions.PublicAsync(seed, terminal, original, token);
    }

    internal static async Task ReplayAsync(PartitionMovementPublicParentRf3Seed seed,
        PartitionMovementLifetimeProofRf3Original original, CancellationToken token)
    {
        await SqlRf3Protocol.EqualAsync(original.Receipt!,
            await McpCallerAssertions.SdkSuccessAsync(await seed.Source.CommitAsync(original.Command, token)));
        var actual = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await seed.Official.CallAsync(
            McpCallerTools.DocumentsCommit, original.Command, token));
        await SqlRf3Protocol.EqualAsync(original.Receipt!, actual.Value);
        await PartitionMovementLifetimeProofRf3Assertions.DocumentAsync(seed, original, token);
    }
}
