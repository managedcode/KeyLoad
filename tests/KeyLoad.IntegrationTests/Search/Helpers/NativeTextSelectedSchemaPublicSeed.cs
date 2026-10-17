using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class NativeTextSelectedSchemaPublicSeed
{
    internal static async Task<NativeTextSelectedSchemaPublicState> CreateAsync(RequestCqrsRf3Callers caller,
        CancellationToken token)
    {
        var scenario = new NativeTextMaintenanceRf3Scenario(NativeTextMaintenanceRf3Scenario.CreatePartition());
        var resource = new ResourceDefinition(NativeTextMaintenanceRf3Scenario.Collection,
            ResourceKind.Collection, scenario.Partition.TransactionDomainId);
        await SqlRf3Protocol.EqualAsync(resource, await McpCallerAssertions.SdkSuccessAsync(
            await caller.Sdk.ConfigureResourceAsync(Guid.NewGuid(), new(scenario.Partition.TenantId,
                scenario.Partition.DatabaseId, resource), token)));
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(resource.Name, NativeTextMaintenanceRf3Scenario.Ukrainian,
                NativeTextMaintenanceRf3Scenario.UkrainianJson, 0L),
             new PutDocument(resource.Name, NativeTextMaintenanceRf3Scenario.English,
                NativeTextMaintenanceRf3Scenario.EnglishJson, 0L)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await caller.Sdk.CommitAsync(command, token));
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(scenario.Partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        MutationReceipt[] mutations = [new("putDocument", resource.Name, NativeTextMaintenanceRf3Scenario.Ukrainian, 1L),
            new("putDocument", resource.Name, NativeTextMaintenanceRf3Scenario.English, 1L)];
        await SqlRf3Protocol.EqualAsync(mutations, receipt.Mutations.ToArray());
        var request = await scenario.RequestAsync(caller.Sdk, caller.Mcp, token);
        var built = await NativeTextMaintenanceRf3Call.ExecuteAsync(caller.Sdk, caller.Mcp,
            request, NativeTextMaintenancePath.Sdk, token);
        await NativeTextMaintenanceRf3Assertions.ResultAsync(built, request);
        await Assert.That(built.Source!.SchemaVersion).IsEqualTo(1L);
        await Assert.That(built.IndexedThroughSequence).IsEqualTo(built.Source.ThroughSequence);
        var state = new NativeTextSelectedSchemaPublicState(scenario, resource, command, receipt, request, built);
        await NativeTextSelectedSchemaPublicAssertions.PagesAsync(caller, state, selected: true, token);
        return state;
    }
}
