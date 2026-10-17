using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Seed
{
    internal static async Task<AdaptiveFilteredPublicRf3State> RunAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers administrator, CancellationToken token)
    {
        var scenario = new NativeAnnMaintenanceRf3Scenario();
        var definition = new ResourceDefinition(NativeAnnMaintenanceRf3Scenario.Collection, ResourceKind.Collection,
            scenario.Partition.TransactionDomainId)
        {
            VectorProfiles = [new(NativeAnnMaintenanceRf3Scenario.Field, NativeAnnMaintenanceRf3Scenario.Space)],
            FieldPolicies = [new(NativeAnnMaintenanceRf3Scenario.Field, AdaptiveFilteredPublicRf3Protocol.Classification,
                RawUseGrant: AdaptiveFilteredPublicRf3Protocol.FieldGrant)]
        };
        await AdaptiveFilteredPublicRf3Equal.RequireAsync(definition, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
                new(scenario.Partition.TenantId, scenario.Partition.DatabaseId, definition), token)));
        var batches = await AdaptiveFilteredPublicRf3SeedCommands.RunAsync(administrator, scenario, token);
        var pin = await scenario.RequestAsync(administrator.Sdk, administrator.Mcp, token);
        var published = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.MaintainAnnIndexAsync(pin, token));
        await AdaptiveFilteredPublicRf3Publication.RequireAsync(published, pin);
        var reader = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            NativeAnnMaintenanceRf3Scenario.Collection, AdaptiveFilteredPublicRf3Protocol.Healthy, token);
        var principal = reader.Principal with
        {
            PolicyEpoch = 2,
            FieldGrants = [AdaptiveFilteredPublicRf3Protocol.FieldGrant]
        };
        await AdaptiveFilteredPublicRf3Equal.RequireAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
        return new(scenario, batches, pin, published, reader with
        {
            Principal = principal
        });
    }
}
