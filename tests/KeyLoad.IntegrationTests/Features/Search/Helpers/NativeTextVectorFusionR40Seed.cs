using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextVectorFusionR40Seed
{
    private const string PutDocumentKind = "putDocument";
    private const string PutVectorKind = "putVector";
    private const string Consumer = "native-text-vector-fusion-consumer";
    internal const string RawReadGrant = "pii.read";

    internal static async Task<NativeTextVectorFusionR40State> CreateAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers admin, CancellationToken token)
    {
        var partition = new PartitionRef("native-text-vector-tenant-" + Guid.NewGuid().ToString("N"),
            "native-text-vector-db", "native-text-vector-domain", Guid.NewGuid().ToString("N"));
        var resource = new ResourceDefinition(NativeTextRf3Scenario.Collection, ResourceKind.Collection,
            partition.TransactionDomainId)
        {
            FieldPolicies = [new(NativeTextRf3Scenario.TextField, "search-text", RawUseGrant: NativeTextRf3Scenario.TextUseGrant),
            new(NativeTextRf3Scenario.VectorField, "search-vector", RawUseGrant: NativeTextRf3Scenario.VectorUseGrant),
            new(NativeTextRf3Scenario.SecretField, "private", RawReadGrant: NativeTextRf3Scenario.SecretReadGrant)]
        };
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, resource), token)));
        var placementRequest = new AtomicPartitionPlacementReadRequest(1, partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadAtomicPartitionPlacementAsync(placementRequest, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await admin.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placementRequest, token))).Value);
        var command = NativeTextRf3Scenario.SeedCommand(partition);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.CommitAsync(command, token));
        await ReceiptAsync(command, receipt, placement);
        var scenario = new NativeTextRf3Scenario(partition, resource, receipt.Token);
        var before = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
        var build = new TextIndexMaintenanceRequest(Guid.NewGuid(), new(partition, Consumer), resource.Name,
            NativeTextRf3Scenario.TextField, 1L, Guid.Parse(before.NodeId),
            new(placement.PhysicalShardId, placement.Incarnation, placement.VoterIds, placement.PlacementEpoch), TextIndexMaintenanceMode.Build);
        var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(admin.Sdk, admin.Mcp, build, NativeTextMaintenancePath.Mcp, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.StatusAsync(token));
        await NativeTextVectorFusionR40Maintenance.FirstAsync(built, build, resource, before, after);
        var principal = new PrincipalRecord("native-text-vector-reader-" + Guid.NewGuid().ToString("N"), partition.TenantId,
            [new(partition.DatabaseId, resource.Name, Capability.Query | Capability.DocumentsRead | Capability.VectorSearch)],
            [RawReadGrant, NativeTextRf3Scenario.SecretReadGrant, NativeTextRf3Scenario.TextUseGrant, NativeTextRf3Scenario.VectorUseGrant])
        { PolicyEpoch = 1L, OwnerId = NativeTextRf3Scenario.OwnerA };
        var reader = await NativeTextRf3Scenario.ConfigureIdentityAsync(fixture, principal, token);
        await SqlRf3Protocol.EqualAsync(principal, reader.Principal);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
        return new(scenario, command, receipt, placement, build, built, reader, outbox);
    }

    private static async Task ReceiptAsync(CommandRequest command, CommitReceipt receipt,
        AtomicPartitionPlacementResolution placement)
    {
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await SqlRf3Protocol.EqualAsync(new CommitToken(placement.Incarnation, command.Partition.AtomicPartitionId,
            receipt.Token.Position, placement.PlacementEpoch), receipt.Token);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var collection = NativeTextRf3Scenario.Collection;
        MutationReceipt[] expected = [new(PutDocumentKind, collection, NativeTextRf3Scenario.FirstId, 1L), new(PutDocumentKind, collection, NativeTextRf3Scenario.SecondId, 1L),
            new(PutDocumentKind, collection, NativeTextRf3Scenario.ThirdId, 1L), new(PutVectorKind, collection, NativeTextRf3Scenario.FirstId, 1L),
            new(PutVectorKind, collection, NativeTextRf3Scenario.SecondId, 1L), new(PutVectorKind, collection, NativeTextRf3Scenario.ThirdId, 1L)];
        await SqlRf3Protocol.EqualAsync(expected, receipt.Mutations.ToArray());
    }
}
