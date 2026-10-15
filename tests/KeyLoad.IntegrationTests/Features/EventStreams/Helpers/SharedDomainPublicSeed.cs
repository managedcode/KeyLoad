using KeyLoad.Client;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.Messaging;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;
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
        var identity = await MessagingRf3Identity.CreateAsync(fixture, tenant,
            resources.Select(resource => new ScopeGrant(Database, resource.Name, Capability.All)).ToArray(), [], false, token);
        var state = new SharedDomainPublicState(scopes, identity);
        await PlacementAsync(fixture, state, token);
        return state;
    }

    internal static async Task PlacementAsync(ClusterFixture fixture, SharedDomainPublicState state, CancellationToken token)
    {
        await SharedDomainPublicSession.RunAsync(fixture, fixture.AdminKey, async (sdk, _) =>
        {
            foreach (var scope in state.Scopes)
            {
                var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(
                    new(PlacementVersion, scope.Partition), token));
                await Assert.That(actual.Partition).IsEqualTo(scope.Partition);
                await Assert.That(actual.VoterIds.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(Rf3Voters);
                await Assert.That(actual.Incarnation).IsNotEqualTo(Guid.Empty);
                if (state.Placements.TryGetValue(scope.Partition.AtomicPartitionId, out var original))
                {
                    await SharedDomainPublicValues.EqualAsync(actual, original);
                }
                else
                {
                    state.Placements.Add(scope.Partition.AtomicPartitionId, actual);
                }
            }
        }, token);
        var first = state.Placements.Values.First();
        foreach (var placement in state.Placements.Values)
        {
            await Assert.That(placement.PhysicalShardId).IsEqualTo(first.PhysicalShardId);
            await Assert.That(placement.Incarnation).IsEqualTo(first.Incarnation);
            await SharedDomainPublicValues.EqualAsync(placement.VoterIds.ToArray(), first.VoterIds.ToArray());
        }
        await Assert.That(state.Scopes.Select(scope => scope.Partition.AtomicPartitionId).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(state.Scopes.Length);
    }
}
