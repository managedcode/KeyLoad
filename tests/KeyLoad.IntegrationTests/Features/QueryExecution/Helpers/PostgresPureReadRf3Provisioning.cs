using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPureReadRf3Provisioning
{
    internal static async Task<PostgresPureReadRf3State> CreateAsync(KeyLoadClient root,
        PostgresCompositionRf3Image image, CancellationToken token)
    {
        var partition = PostgresRf3State.Partition;
        var identity = await PostgresRf3Seed.IdentityAsync(root, PostgresPureReadRf3Protocol.Marker,
            Capability.GraphRead | Capability.Query, token, PostgresCompositionRf3Protocol.Graph);
        var expected = identity.Principal with
        {
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + PostgresPureReadRf3Protocol.EpochStep),
            Grants =
            [
                new(partition.DatabaseId, RelationalSqlRf3JoinBudgetData.LeftCollection, Capability.DocumentsRead | Capability.Query),
                new(partition.DatabaseId, RelationalSqlRf3JoinBudgetData.RightCollection, Capability.DocumentsRead | Capability.Query),
                new(partition.DatabaseId, PostgresCompositionRf3Protocol.SourceQueue, Capability.QueueInspect | Capability.Query),
                new(partition.DatabaseId, PostgresCompositionRf3Protocol.TargetQueue, Capability.QueueInspect | Capability.Query),
                new(partition.DatabaseId, PostgresCompositionRf3Protocol.Graph, Capability.GraphRead | Capability.Query)
            ]
        };
        var actual = await PersistAsync(root, expected, token);
        return new(identity with { Principal = actual }, actual, image);
    }

    internal static Task<PrincipalRecord> GraphPolicyAsync(KeyLoadClient root, PrincipalRecord current,
        bool allowed, CancellationToken token)
        => PersistAsync(root, current with
        {
            PolicyEpoch = checked(current.PolicyEpoch + PostgresPureReadRf3Protocol.EpochStep),
            Grants = current.Grants.Select(grant => grant.Resource == PostgresCompositionRf3Protocol.Graph
                ? grant with
                {
                    Capabilities = allowed ? grant.Capabilities | Capability.GraphRead
                    : grant.Capabilities & ~Capability.GraphRead
                } : grant).ToImmutableArray()
        }, token);

    private static async Task<PrincipalRecord> PersistAsync(KeyLoadClient root, PrincipalRecord expected, CancellationToken token)
    {
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
        return actual;
    }
}
