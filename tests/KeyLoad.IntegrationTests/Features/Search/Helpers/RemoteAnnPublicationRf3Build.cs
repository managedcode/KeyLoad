using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class RemoteAnnPublicationRf3Build
{
    internal static async Task<RemoteAnnPublicationRf3State> SeedAsync(KeyLoadClient sdk,
        McpOfficialClient official, CancellationToken token)
    {
        var scenario = new NativeAnnMaintenanceRf3Scenario();
        var definition = new ResourceDefinition(NativeAnnMaintenanceRf3Scenario.Collection,
            ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        {
            VectorProfiles = [new(NativeAnnMaintenanceRf3Scenario.Field, NativeAnnMaintenanceRf3Scenario.Space)],
            FieldPolicies = [new(NativeAnnMaintenanceRf3Scenario.Field, RemoteAnnPublicationRf3Protocol.Classification,
                RemoteAnnPublicationRf3Protocol.RawRead, RemoteAnnPublicationRf3Protocol.RawUse,
                RemoteAnnPublicationRf3Protocol.FieldWrite)]
        };
        _ = await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, definition), token));
        var seed = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[0], "{}"),
             new PutDocument(NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[1], "{}"),
             new PutDocument(NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[2], "{}"),
             new PutVector(NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[0],
                 NativeAnnMaintenanceRf3Scenario.Field, [1, 0], NativeAnnMaintenanceRf3Scenario.Space, RemoteAnnPublicationRf3Protocol.FirstEpoch),
             new PutVector(NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[1],
                 NativeAnnMaintenanceRf3Scenario.Field, [0, 1], NativeAnnMaintenanceRf3Scenario.Space, RemoteAnnPublicationRf3Protocol.FirstEpoch),
             new PutVector(NativeAnnMaintenanceRf3Scenario.Collection, NativeAnnMaintenanceRf3Scenario.Ids[2],
                 NativeAnnMaintenanceRf3Scenario.Field, [-1, 0], NativeAnnMaintenanceRf3Scenario.Space, RemoteAnnPublicationRf3Protocol.FirstEpoch)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seed, token));
        var original = (await scenario.RequestAsync(sdk, official, token)) with
        {
            MaintenanceProfile = new(NativeAnnMaintenanceRf3Scenario.Collection,
            NativeAnnMaintenanceRf3Scenario.Field, NativeAnnMaintenanceRf3Scenario.Space)
        };
        var completed = await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(original, token));
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(completed, original);
        return new(scenario, original, completed, seed, receipt);
    }

    internal static async Task<RemoteAnnPublicationRf3State> RebuildMovedAsync(KeyLoadClient sdk,
        McpOfficialClient official, RemoteAnnPublicationRf3State previous, CancellationToken token)
    {
        var current = await previous.Scenario.RequestAsync(sdk, official, token);
        var original = current with
        {
            Consumer = current.Consumer with
            {
                Name = RemoteAnnPublicationRf3Protocol.MovedConsumer
            },
            MaintenanceProfile = previous.Original.MaintenanceProfile
        };
        await Assert.That(original.NodeId).IsNotEqualTo(previous.Original.NodeId);
        await Assert.That(original.Placement.PhysicalShardId).IsNotEqualTo(previous.Original.Placement.PhysicalShardId);
        var completed = await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(original, token));
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(completed, original);
        return previous with
        {
            Original = original,
            Completed = completed
        };
    }
}
