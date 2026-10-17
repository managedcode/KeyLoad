using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextReleasedRepairPublicSeed
{
    internal static async Task<NativeTextSelectedSchemaPublicState> CreateAsync(RequestCqrsRf3Callers caller, CancellationToken token)
    {
        var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        var resource = new ResourceDefinition(NativeTextMaintenanceRf3Scenario.Collection,
            ResourceKind.Collection, scenario.Partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(scenario.Partition.TenantId,
                scenario.Partition.DatabaseId, resource), token)));
        var build = await scenario.RequestAsync(caller.Sdk, caller.Mcp, token);
        var placementRequest = new AtomicPartitionPlacementReadRequest(1, scenario.Partition);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.ReadAtomicPartitionPlacementAsync(placementRequest, token));
        await SqlRf3Protocol.EqualAsync(placement, (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(
            await caller.Mcp.CallAsync(McpCallerTools.AdminPartitionPlacementRead, placementRequest, token))).Value);
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(resource.Name, NativeTextMaintenanceRf3Scenario.Ukrainian, NativeTextMaintenanceRf3Scenario.UkrainianJson, 0L),
             new PutDocument(resource.Name, NativeTextMaintenanceRf3Scenario.English, NativeTextMaintenanceRf3Scenario.EnglishJson, 0L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(scenario.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        MutationReceipt[] expected = [new("putDocument", resource.Name, NativeTextMaintenanceRf3Scenario.Ukrainian, 1L),
            new("putDocument", resource.Name, NativeTextMaintenanceRf3Scenario.English, 1L)];
        await SqlRf3Protocol.EqualAsync(expected, receipt.Mutations.ToArray());
        var before = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp, build, NativeTextMaintenancePath.Sdk, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.StatusAsync(token));
        await NativeTextReleasedRepairPublicAssertions.FirstAsync(built, build, before, after);
        var state = new NativeTextSelectedSchemaPublicState(scenario, resource, command, receipt, build, built);
        await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, state, true, token);
        return state;
    }
}
