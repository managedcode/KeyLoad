using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.Messaging;
using KeyLoad.IntegrationTests.ResourceExecution;
using KeyLoad.RecoveryTests.EventStreams;
using static KeyLoad.IntegrationTests.EventStreams.SharedDomainPublicProtocol;
using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class SharedDomainPublicSeed
{
    internal static async Task<SharedDomainPublicState> CreateAsync(ClusterFixture fixture, CancellationToken token)
    {
        var tenant = TenantPrefix + Guid.NewGuid().ToString(GuidFormat);
        var scopes = SharedDomainCorpus.Create(tenant, Database);
        var resources = scopes.SelectMany(scope => scope.Resources).ToArray();
        var lifetimes = new Dictionary<string, EntityRefLifetime>(StringComparer.Ordinal);
        await SharedDomainPublicSession.RunAsync(fixture, fixture.AdminKey, async (sdk, _) =>
        {
            foreach (var resource in resources)
            {
                var creationId = Guid.NewGuid();
                var creation = new ConfigureResourceRequest(tenant, Database, resource);
                await McpCallerAssertions.SdkSuccessAsync(await sdk.ConfigureResourceAsync(creationId, creation, token));
                lifetimes.Add(resource.Name, ResourceCreationLifetime.Create(creationId, creation));
            }
        }, token);
        var identity = await MessagingClusterIdentity.CreateAsync(fixture, tenant,
            resources.Select(resource => new ScopeGrant(Database, resource.Name, Capability.All)).ToArray(), [], false, token);
        var state = new SharedDomainPublicState(scopes, identity);
        foreach (var (name, lifetime) in lifetimes)
        {
            state.Lifetimes.Add(name, lifetime);
        }
        await Assert.That(state.Scopes.Select(scope => scope.Partition.AtomicPartitionId).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(state.Scopes.Length);
        return state;
    }
}
