using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record NativeAnnPublicColdSeed(NativeAnnMaintenanceRf3Scenario Scenario,
    CommandRequest Command, CommitReceipt Receipt, AnnMaintenanceRequest Pin, AnnMaintenanceResult Published,
    McpPersistedIdentity Reader)
{
    private const long Revision = 1;
    private const Capability Grants = Capability.Query | Capability.DocumentsRead | Capability.VectorSearch;

    internal static async Task<NativeAnnPublicColdSeed> CreateAsync(ClusterFixture fixture,
        KeyLoadClient sdk, McpOfficialClient mcp, CancellationToken token)
    {
        var scenario = new NativeAnnMaintenanceRf3Scenario();
        var definition = new ResourceDefinition(NativeAnnMaintenanceRf3Scenario.Collection,
            ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        { VectorProfiles = [new(NativeAnnMaintenanceRf3Scenario.Field, NativeAnnMaintenanceRf3Scenario.Space)] };
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, definition), token));
        var ids = NativeAnnMaintenanceRf3Scenario.Ids;
        var collection = NativeAnnMaintenanceRf3Scenario.Collection;
        var field = NativeAnnMaintenanceRf3Scenario.Field;
        var space = NativeAnnMaintenanceRf3Scenario.Space;
        var command = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(collection, ids[0], "{}"), new PutDocument(collection, ids[1], "{}"),
             new PutDocument(collection, ids[2], "{}"), new PutVector(collection, ids[0], field, [1, 0], space, Revision),
             new PutVector(collection, ids[1], field, [0, 1], space, Revision),
             new PutVector(collection, ids[2], field, [-1, 0], space, Revision)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, token));
        var pin = await scenario.RequestAsync(sdk, mcp, token);
        var published = await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(pin, token));
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(published, pin);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, collection, Grants, token);
        return new(scenario, command, receipt, pin, published, reader);
    }
}
