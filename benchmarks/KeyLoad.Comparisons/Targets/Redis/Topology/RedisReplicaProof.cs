using System.Net;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisReplicaProof
{
    private const string AofSetting = "appendonly", AofFlushSetting = "appendfsync", AofEnabled = "yes", AofAlways = "always";
    private const string ConfigCommand = "CONFIG", GetCommand = "GET";
    private const string ErrorEndpoints = "RedisReplicaEndpointsInvalid";
    private const string ErrorPrimaryRole = "RedisPrimaryRoleOrMembershipInvalid", ErrorReplicaEndpoint = "RedisReplicaConfiguredEndpointInvalid";
    private const string ErrorReplicaIsPrimary = "RedisReplicaEndpointIsPrimary", ErrorReplicaIdentity = "RedisReplicaPrimaryIdentityMismatch";
    private const string ErrorReplicaDistinct = "RedisReplicaEndpointsNotDistinct";
    private const string ErrorAof = "RedisAofAlwaysRequired";
    private const string SingleState = "single primary";
    private const string ReplicatedState = "single primary with verified execution direct replicas";

    public static ConfigurationOptions CreateOptions(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AllowAdmin = true;
        return options;
    }

    public static Task<RedisNodeIdentity> ReadIdentityAsync(ConnectionMultiplexer connection, EndPoint endpoint, CancellationToken token)
        => RedisNodeIdentity.ReadAsync(connection.GetServer(endpoint), CommandFlags.DemandMaster, token);

    public static async Task<ClusterEvidence> VerifyAsync(ConnectionMultiplexer primary, string[] replicaStrings, ComparisonTopology topology, RedisNodeIdentity primaryIdentity, string probeKey, string payload, IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<ComparisonDiagnosticOptions> diagnosticOptions, TimeProvider timeProvider, CancellationToken token)
    {
        const int AdjacentElementOffset = 1;
        const int NoObservedItems = 0;
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;

        var diagnostics = new RedisReplicaDiagnostics(diagnosticOptions);
        var requiredReplicas = ComparisonTopologies.NodeCount(topology) - AdjacentElementOffset;
        var replicated = requiredReplicas > NoObservedItems;
        if (replicaStrings.Length != requiredReplicas)
        {
            throw new ComparisonFailureException(ErrorEndpoints);
        }

        var primaryEndpoint = RedisProtocol.RequirePrimaryEndpoint(primary);
        var primaryServer = primary.GetServer(primaryEndpoint);
        await VerifyAofAsync(primaryServer, CommandFlags.DemandMaster, token);
        var replicationId = await VerifyPrimaryAsync(primaryServer, requiredReplicas, token);
        RedisNodeIdentity.RequireUnchanged(await RedisNodeIdentity.ReadAsync(primaryServer, CommandFlags.DemandMaster, token), primaryIdentity);
        if (!replicated)
        {
            return RedisNodeIdentity.SingleEvidence(primaryIdentity, SingleState);
        }

        await primary.GetDatabase().StringSetAsync(probeKey, payload, lifecycleOptions.Value.RedisProbeExpiry, flags: CommandFlags.DemandMaster).WaitAsync(token);
        var replicas = await ConnectReplicasAsync(replicaStrings, token);
        try
        {
            var endpoints = VerifyReplicaEndpoints(primaryEndpoint, replicas);
            var identities = await ReadReplicaIdentitiesAsync(replicas, endpoints, primaryEndpoint, token);
            var allIdentities = new[] { primaryIdentity }.Concat(identities).ToArray();
            RedisNodeIdentity.RequireUniqueVersionedSet(allIdentities, primaryIdentity, requiredReplicas + SingleItemCount);
            await RedisCopyObservation.VerifyDirectCopiesAsync(replicas: replicas, endpoints: endpoints,
                database: primary.GetDatabase().Database, key: probeKey, payload: payload, token: token, lifecycleOptions: lifecycleOptions, timeProvider: timeProvider);
            for (var index = FirstElementIndex; index < replicas.Length; index++)
            {
                await VerifyReplicaAsync(replicas[index].GetServer(endpoints[index]), endpoints[index], primaryEndpoint,
                    replicationId, diagnostics, token);
            }

            await VerifyIdentitiesUnchangedAsync(primaryServer, primaryIdentity, replicas, endpoints, identities, token);
            return RedisNodeIdentity.ReplicatedEvidence(primaryIdentity, identities, ReplicatedState);
        }
        finally
        {
            foreach (var replica in replicas)
            {
                await replica.DisposeAsync();
            }
        }
    }

    public static Task<string> VerifyWorkerPrimaryAsync(ConnectionMultiplexer connection, ComparisonTopology topology, CancellationToken token)
    {
        const int SingleItemCount = 1;

        var endpoint = RedisProtocol.RequirePrimaryEndpoint(connection);
        return VerifyPrimaryAsync(connection.GetServer(endpoint), ComparisonTopologies.NodeCount(topology) - SingleItemCount, token);
    }

    private static async Task VerifyAofAsync(IServer server, CommandFlags flags, CancellationToken token)
    {
        const int FirstElementIndex = 0;
        const int PropertyPairWidth = 2;
        const int AdjacentElementOffset = 1;

        var args = new object[] { GetCommand, AofSetting, AofFlushSetting };
        var result = (RedisResult[])(await server.ExecuteAsync(ConfigCommand, args, flags).WaitAsync(token))!;
        var values = Enumerable.Range(FirstElementIndex, result.Length / PropertyPairWidth).ToDictionary(index => result[index * PropertyPairWidth].ToString(), index => result[index * PropertyPairWidth + AdjacentElementOffset].ToString());
        if (values.GetValueOrDefault(AofSetting) != AofEnabled || values.GetValueOrDefault(AofFlushSetting) != AofAlways)
        {
            throw new ComparisonFailureException(ErrorAof);
        }
    }

    private static async Task<string> VerifyPrimaryAsync(IServer server, int expectedReplicas, CancellationToken token)
    {
        const int NoItems = 0;

        var info = await RedisProtocol.ReadInfoAsync(server, RedisProtocol.ReplicationSection, CommandFlags.DemandMaster, token);
        var role = await RedisProtocol.ReadRoleAsync(server, CommandFlags.DemandMaster, token);
        var replicationId = info.GetValueOrDefault(RedisProtocol.ReplicationIdField);
        if (info.GetValueOrDefault(RedisProtocol.RoleField) != RedisProtocol.MasterRole ||
            RedisProtocol.ParseInteger(info, RedisProtocol.ReplicaCountField) != expectedReplicas ||
            role.Length == NoItems || role[RedisProtocol.RoleNameIndex].ToString() != RedisProtocol.MasterRole ||
            !RedisProtocol.ValidReplicationId(replicationId))
        {
            throw new ComparisonFailureException(ErrorPrimaryRole);
        }
        return replicationId!;
    }

    private static async Task<ConnectionMultiplexer[]> ConnectReplicasAsync(string[] connectionStrings, CancellationToken token)
    {
        const int SingleItemCount = 1;

        var connections = new List<ConnectionMultiplexer>(connectionStrings.Length);
        try
        {
            foreach (var connectionString in connectionStrings)
            {
                var options = CreateOptions(connectionString);
                if (options.EndPoints.Count != SingleItemCount)
                {
                    throw new ComparisonFailureException(ErrorReplicaEndpoint);
                }

                connections.Add(await ConnectionMultiplexer.ConnectAsync(options).WaitAsync(token));
            }
            return connections.ToArray();
        }
        catch (Exception)
        {
            foreach (var connection in connections)
            {
                await connection.DisposeAsync();
            }

            throw;
        }
    }

    private static EndPoint[] VerifyReplicaEndpoints(EndPoint primary, ConnectionMultiplexer[] replicas)
    {
        var endpoints = replicas.Select(connection => RedisProtocol.ConfiguredEndpoint(connection, ErrorReplicaEndpoint)).ToArray();
        if (endpoints.Select(RedisProtocol.EndpointIdentity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != replicas.Length ||
            endpoints.Any(endpoint => RedisProtocol.SameEndpoint(endpoint, primary)))
        {
            throw new ComparisonFailureException(ErrorReplicaDistinct);
        }

        return endpoints;
    }

    private static async Task<RedisNodeIdentity[]> ReadReplicaIdentitiesAsync(ConnectionMultiplexer[] replicas,
        EndPoint[] endpoints, EndPoint primary, CancellationToken token)
    {
        const int FirstElementIndex = 0;

        var identities = new RedisNodeIdentity[replicas.Length];
        for (var index = FirstElementIndex; index < replicas.Length; index++)
        {
            var endpoint = endpoints[index];
            if (RedisProtocol.SameEndpoint(endpoint, primary))
            {
                throw new ComparisonFailureException(ErrorReplicaIsPrimary);
            }

            identities[index] = await RedisNodeIdentity.ReadAsync(replicas[index].GetServer(endpoint), CommandFlags.DemandReplica, token);
        }
        return identities;
    }

    private static async Task VerifyIdentitiesUnchangedAsync(IServer primaryServer, RedisNodeIdentity primaryIdentity,
        ConnectionMultiplexer[] replicas, EndPoint[] endpoints, RedisNodeIdentity[] replicaIdentities, CancellationToken token)
    {
        const int FirstElementIndex = 0;
        const int SingleItemCount = 1;

        RedisNodeIdentity.RequireUnchanged(
            await RedisNodeIdentity.ReadAsync(primaryServer, CommandFlags.DemandMaster, token), primaryIdentity);
        for (var index = FirstElementIndex; index < replicas.Length; index++)
        {
            var server = replicas[index].GetServer(endpoints[index]);
            var current = await RedisNodeIdentity.ReadAsync(server, CommandFlags.DemandReplica, token);
            RedisNodeIdentity.RequireUnchanged(current, replicaIdentities[index]);
        }
        RedisNodeIdentity.RequireUniqueVersionedSet(new[] { primaryIdentity }.Concat(replicaIdentities).ToArray(),
            primaryIdentity, replicas.Length + SingleItemCount);
    }

    // Replicas name the primary by its container-network address, so the client-visible primary endpoint is not the oracle;
    // the replica must follow the primary's own replication id.
    private static async Task VerifyReplicaAsync(IServer server, EndPoint endpoint, EndPoint primary,
        string replicationId, RedisReplicaDiagnostics diagnostics, CancellationToken token)
    {
        const int SingleItemCount = 1;

        if (RedisProtocol.SameEndpoint(endpoint, primary))
        {
            throw new ComparisonFailureException(ErrorReplicaIsPrimary);
        }

        await VerifyAofAsync(server, CommandFlags.DemandReplica, token);
        var info = await RedisProtocol.ReadInfoAsync(server, RedisProtocol.ReplicationSection, CommandFlags.DemandReplica, token);
        var role = await RedisProtocol.ReadRoleAsync(server, CommandFlags.DemandReplica, token);
        var host = info.GetValueOrDefault(RedisProtocol.MasterHostField);
        var port = RedisProtocol.ParseInteger(info, RedisProtocol.MasterPortField);
        if (info.GetValueOrDefault(RedisProtocol.RoleField) != RedisProtocol.ReplicaRole ||
            info.GetValueOrDefault(RedisProtocol.LinkField) != RedisProtocol.LinkUp || host is null || port < SingleItemCount ||
            role.Length < RedisProtocol.RoleReplicaResponseLength || role[RedisProtocol.RoleNameIndex].ToString() != RedisProtocol.ReplicaRole ||
            !StringComparer.OrdinalIgnoreCase.Equals(host, role[RedisProtocol.RoleHostIndex].ToString()) ||
            !int.TryParse(role[RedisProtocol.RolePortIndex].ToString(), out var rolePort) || rolePort != port ||
            !string.Equals(info.GetValueOrDefault(RedisProtocol.ReplicationIdField), replicationId, StringComparison.Ordinal))
        {
            diagnostics.WriteFailure(endpoint, primary, replicationId, info, role);
            throw new ComparisonFailureException(ErrorReplicaIdentity);
        }
    }

}
