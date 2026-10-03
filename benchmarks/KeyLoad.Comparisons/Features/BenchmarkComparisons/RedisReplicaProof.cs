using System.Net;
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
    private const string ReplicatedState = "single primary with verified native direct replicas";
    private const int ProbeExpiryMinutes = 2;

    public static ConfigurationOptions CreateOptions(string connectionString)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.AllowAdmin = true;
        return options;
    }

    public static Task<RedisNodeIdentity> ReadIdentityAsync(ConnectionMultiplexer connection, EndPoint endpoint, CancellationToken token)
        => RedisNodeIdentity.ReadAsync(connection.GetServer(endpoint), CommandFlags.DemandMaster, token);

    public static async Task<ClusterEvidence> VerifyAsync(ConnectionMultiplexer primary, string[] replicaStrings,
        ComparisonTopology topology, RedisNodeIdentity primaryIdentity, string probeKey, string payload, CancellationToken token)
    {
        var requiredReplicas = ComparisonTopologies.NodeCount(topology) - 1;
        var replicated = requiredReplicas > 0;
        if (replicaStrings.Length != requiredReplicas)
        {
            throw new ComparisonFailureException(ErrorEndpoints);
        }

        var primaryEndpoint = RedisNativeProtocol.RequirePrimaryEndpoint(primary);
        var primaryServer = primary.GetServer(primaryEndpoint);
        await VerifyAofAsync(primaryServer, CommandFlags.DemandMaster, token);
        await VerifyPrimaryAsync(primaryServer, requiredReplicas, token);
        RedisNodeIdentity.RequireUnchanged(await RedisNodeIdentity.ReadAsync(primaryServer, CommandFlags.DemandMaster, token), primaryIdentity);
        if (!replicated)
        {
            return RedisNodeIdentity.SingleEvidence(primaryIdentity, SingleState);
        }

        await primary.GetDatabase().StringSetAsync(probeKey, payload, TimeSpan.FromMinutes(ProbeExpiryMinutes), flags: CommandFlags.DemandMaster).WaitAsync(token);
        var replicas = await ConnectReplicasAsync(replicaStrings, token);
        try
        {
            var endpoints = VerifyReplicaEndpoints(primaryEndpoint, replicas);
            var identities = await ReadReplicaIdentitiesAsync(replicas, endpoints, primaryEndpoint, token);
            var allIdentities = new[] { primaryIdentity }.Concat(identities).ToArray();
            RedisNodeIdentity.RequireUniqueVersionedSet(allIdentities, primaryIdentity, requiredReplicas + 1);
            await RedisCopyObservation.VerifyDirectCopiesAsync(replicas, endpoints, primary.GetDatabase().Database,
                probeKey, payload, token);
            for (var index = 0; index < replicas.Length; index++)
            {
                await VerifyReplicaAsync(replicas[index].GetServer(endpoints[index]), endpoints[index], primaryEndpoint, token);
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

    public static async Task VerifyWorkerPrimaryAsync(ConnectionMultiplexer connection, ComparisonTopology topology, CancellationToken token)
    {
        var endpoint = RedisNativeProtocol.RequirePrimaryEndpoint(connection);
        await VerifyPrimaryAsync(connection.GetServer(endpoint), ComparisonTopologies.NodeCount(topology) - 1, token);
    }

    private static async Task VerifyAofAsync(IServer server, CommandFlags flags, CancellationToken token)
    {
        var args = new object[] { GetCommand, AofSetting, AofFlushSetting };
        var result = (RedisResult[])(await server.ExecuteAsync(ConfigCommand, args, flags).WaitAsync(token))!;
        var values = Enumerable.Range(0, result.Length / 2).ToDictionary(index => result[index * 2].ToString(), index => result[index * 2 + 1].ToString());
        if (values.GetValueOrDefault(AofSetting) != AofEnabled || values.GetValueOrDefault(AofFlushSetting) != AofAlways)
        {
            throw new ComparisonFailureException(ErrorAof);
        }
    }

    private static async Task VerifyPrimaryAsync(IServer server, int expectedReplicas, CancellationToken token)
    {
        var info = await RedisNativeProtocol.ReadInfoAsync(server, RedisNativeProtocol.ReplicationSection, CommandFlags.DemandMaster, token);
        var role = await RedisNativeProtocol.ReadRoleAsync(server, CommandFlags.DemandMaster, token);
        if (info.GetValueOrDefault(RedisNativeProtocol.RoleField) != RedisNativeProtocol.MasterRole ||
            RedisNativeProtocol.ParseInteger(info, RedisNativeProtocol.ReplicaCountField) != expectedReplicas ||
            role.Length == 0 || role[RedisNativeProtocol.RoleNameIndex].ToString() != RedisNativeProtocol.MasterRole)
        {
            throw new ComparisonFailureException(ErrorPrimaryRole);
        }
    }

    private static async Task<ConnectionMultiplexer[]> ConnectReplicasAsync(string[] connectionStrings, CancellationToken token)
    {
        var connections = new List<ConnectionMultiplexer>(connectionStrings.Length);
        try
        {
            foreach (var connectionString in connectionStrings)
            {
                var options = CreateOptions(connectionString);
                if (options.EndPoints.Count != 1)
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
        var endpoints = replicas.Select(connection => RedisNativeProtocol.ConfiguredEndpoint(connection, ErrorReplicaEndpoint)).ToArray();
        if (endpoints.Select(RedisNativeProtocol.EndpointIdentity).Distinct(StringComparer.OrdinalIgnoreCase).Count() != replicas.Length ||
            endpoints.Any(endpoint => RedisNativeProtocol.SameEndpoint(endpoint, primary)))
        {
            throw new ComparisonFailureException(ErrorReplicaDistinct);
        }

        return endpoints;
    }

    private static async Task<RedisNodeIdentity[]> ReadReplicaIdentitiesAsync(ConnectionMultiplexer[] replicas,
        EndPoint[] endpoints, EndPoint primary, CancellationToken token)
    {
        var identities = new RedisNodeIdentity[replicas.Length];
        for (var index = 0; index < replicas.Length; index++)
        {
            var endpoint = endpoints[index];
            if (RedisNativeProtocol.SameEndpoint(endpoint, primary))
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
        RedisNodeIdentity.RequireUnchanged(
            await RedisNodeIdentity.ReadAsync(primaryServer, CommandFlags.DemandMaster, token), primaryIdentity);
        for (var index = 0; index < replicas.Length; index++)
        {
            var server = replicas[index].GetServer(endpoints[index]);
            var current = await RedisNodeIdentity.ReadAsync(server, CommandFlags.DemandReplica, token);
            RedisNodeIdentity.RequireUnchanged(current, replicaIdentities[index]);
        }
        RedisNodeIdentity.RequireUniqueVersionedSet(new[] { primaryIdentity }.Concat(replicaIdentities).ToArray(),
            primaryIdentity, replicas.Length + 1);
    }

    private static async Task VerifyReplicaAsync(IServer server, EndPoint endpoint, EndPoint primary, CancellationToken token)
    {
        if (RedisNativeProtocol.SameEndpoint(endpoint, primary))
        {
            throw new ComparisonFailureException(ErrorReplicaIsPrimary);
        }

        await VerifyAofAsync(server, CommandFlags.DemandReplica, token);
        var info = await RedisNativeProtocol.ReadInfoAsync(server, RedisNativeProtocol.ReplicationSection, CommandFlags.DemandReplica, token);
        var role = await RedisNativeProtocol.ReadRoleAsync(server, CommandFlags.DemandReplica, token);
        var host = info.GetValueOrDefault(RedisNativeProtocol.MasterHostField);
        var port = RedisNativeProtocol.ParseInteger(info, RedisNativeProtocol.MasterPortField);
        if (info.GetValueOrDefault(RedisNativeProtocol.RoleField) != RedisNativeProtocol.ReplicaRole ||
            info.GetValueOrDefault(RedisNativeProtocol.LinkField) != RedisNativeProtocol.LinkUp || host is null || port < 1 ||
            role.Length < RedisNativeProtocol.RoleReplicaResponseLength || role[RedisNativeProtocol.RoleNameIndex].ToString() != RedisNativeProtocol.ReplicaRole ||
            !StringComparer.OrdinalIgnoreCase.Equals(host, role[RedisNativeProtocol.RoleHostIndex].ToString()) ||
            !int.TryParse(role[RedisNativeProtocol.RolePortIndex].ToString(), out var rolePort) || rolePort != port ||
            !RedisNativeProtocol.EndpointMatches(primary, host, port))
        {
            RedisReplicaDiagnostics.WriteFailure(endpoint, primary, info, role);
            throw new ComparisonFailureException(ErrorReplicaIdentity);
        }
    }

}
