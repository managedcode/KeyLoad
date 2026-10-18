using KeyLoad.RecoveryTests.Features.EventStreams;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.Messaging;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;
using static KeyLoad.IntegrationTests.Features.EventStreams.SharedDomainPublicProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class SharedDomainPublicSeed
{
    internal static async Task<SharedDomainPublicState> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var tenant = TenantPrefix + Guid.NewGuid().ToString(GuidFormat);
        var scopes = SharedDomainCorpus.Create(tenant, Database);
        var resources = scopes.SelectMany(scope => scope.Resources).ToArray();
        await SharedDomainPublicSession.RunAsync(fixture, fixture.AdminKey, async (sdk, _) =>
        {
            foreach (var resource in resources)
            {
                await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(
                    Guid.NewGuid(), new(tenant, Database, resource), token));
            }
        }, token);
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            resources.Select(resource => new ScopeGrant(Database, resource.Name, Capability.All)).ToArray(), [], false, token);
        var state = new SharedDomainPublicState(scopes, identity);
        await Assert.That(state.Scopes.Select(scope => scope.Partition.AtomicPartitionId).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(state.Scopes.Length);
        return state;
    }
}
