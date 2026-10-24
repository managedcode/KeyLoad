using System.Collections.Immutable;
using System.Text;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace KeyLoad.Comparisons.Targets;

internal static class RedisDocumentCopyProof
{
    private const int PrimaryCopyCount = 1, PrimaryIdentityCount = 1, NoReplicas = 0, FirstReplicaIndex = 0;
    private const int ConfigPairWidth = 2, NoRemainder = 0, FirstConfigField = 0, ConfigValueOffset = 1;
    private const int ReceiptFieldCount = 2, LocalReceiptIndex = 0, ReplicaReceiptIndex = 1, RequiredLocalFsync = 1;
    private const string SingleState = "single primary", ReplicatedState = "final execution direct replicas";
    private const string ConfigCommand = "CONFIG", GetCommand = "GET", AofSetting = "appendonly", AofFlushSetting = "appendfsync";
    private const string AofEnabled = "yes", AofAlways = "always", ClientCommand = "CLIENT", ClientIdSubcommand = "ID", WaitAofCommand = "WAITAOF";
    private const string FinalReplicaObservation = "Final replica state identity={0}; records={1}; sha256={2}; execution DemandReplica full ordered readback";
    private const string FinalFsyncObservation = "Final document state records={0}; every execution replica full ordered readback; WAITAOF local=1 replicas={1}; final fence on unchanged CLIENT ID";
    private const string Failure = "RedisFinalDocumentCopyReceiptMissing";
    private static readonly CompositeFormat FinalReplicaObservationFormat = CompositeFormat.Parse(FinalReplicaObservation);
    private static readonly CompositeFormat FinalFsyncObservationFormat = CompositeFormat.Parse(FinalFsyncObservation);
    internal static async Task<ClusterEvidence> VerifyAsync(ConnectionMultiplexer primary, string[] endpoints,
        ComparisonTopology topology, TargetProfile initial, string fenceKey, DocumentComparisonSchedule schedule,
        IOptions<ComparisonLifecycleOptions> lifecycleOptions, TimeProvider timeProvider,
        Func<ConnectionMultiplexer, CancellationToken, IAsyncEnumerable<FoundDocument>> readReplica, CancellationToken token)
    {
        using var deadline = new ComparisonCancellationSource(timeProvider, token);
        deadline.CancelAfter(lifecycleOptions.Value.ReadinessTimeout);
        var replicas = new List<ConnectionMultiplexer>(endpoints.Length);
        var primaryEndpoint = RedisProtocol.RequirePrimaryEndpoint(primary);
        var primaryServer = primary.GetServer(primaryEndpoint);
        var primaryIdentity = await JoinAsync(RedisNodeIdentity.ReadAsync(primaryServer, CommandFlags.DemandMaster, CancellationToken.None), deadline.Token).ConfigureAwait(false);
        var copies = ComparisonTopologies.NodeCount(topology);
        if (endpoints.Length != copies - PrimaryCopyCount)
        {
            throw new ComparisonFailureException(Failure);
        }

        await RequireAofAsync(primaryServer, CommandFlags.DemandMaster, deadline.Token).ConfigureAwait(false);
        var replicationId = await JoinAsync(RedisReplicaProof.VerifyWorkerPrimaryAsync(primary, topology, CancellationToken.None), deadline.Token).ConfigureAwait(false);
        var database = primary.GetDatabase();
        var fenced = false;
        try
        {
            var identities = await ObserveReplicasAsync(primaryEndpoint, replicationId, primaryIdentity, endpoints, replicas, deadline.Token).ConfigureAwait(false);
            RedisNodeIdentity.RequireUniqueVersionedSet(identities, primaryIdentity, copies);
            var evidence = replicas.Count == NoReplicas ? RedisNodeIdentity.SingleEvidence(primaryIdentity, SingleState)
                : RedisNodeIdentity.ReplicatedEvidence(primaryIdentity, identities.Skip(PrimaryIdentityCount).ToArray(), ReplicatedState);
            RequireInitialIdentities(initial.Cluster ?? throw new ComparisonFailureException(Failure), evidence);
            var clientId = await ClientIdAsync(database, deadline.Token).ConfigureAwait(false);
            // A write after all workload clients joined places the final corpus behind one global replication offset.
            if (!await JoinAsync(database.StringSetAsync(fenceKey, DocumentComparisonOracle.Digest(schedule.FinalDocuments()),
                when: When.NotExists, flags: CommandFlags.DemandMaster), deadline.Token).ConfigureAwait(false))
            {
                throw new ComparisonFailureException(Failure);
            }

            fenced = true;
            await RequireAllFsyncAsync(database, copies - PrimaryCopyCount, clientId, lifecycleOptions.Value.RedisReceiptTimeout, deadline.Token).ConfigureAwait(false);
            var observations = evidence.Observations.ToList();
            await VerifyReplicaReadbacksAsync(replicas, identities, replicationId, schedule, readReplica, observations, token).ConfigureAwait(false);
            await RequireFinalPrimaryAsync(primary, primaryServer, topology, replicationId, primaryIdentity, token).ConfigureAwait(false);
            return evidence with
            {
                Observations = observations.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, FinalFsyncObservationFormat, schedule.Final, copies - PrimaryCopyCount)).ToImmutableArray()
            };
        }
        finally
        {
            try
            {
                if (fenced)
                {
                    _ = await database.KeyDeleteAsync(fenceKey, CommandFlags.DemandMaster).ConfigureAwait(false);
                }
            }
            finally
            {
                foreach (var replica in replicas)
                {
                    await replica.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }
    private static async Task RequireFinalPrimaryAsync(ConnectionMultiplexer primary, IServer primaryServer, ComparisonTopology topology,
        string replicationId, RedisNodeIdentity primaryIdentity, CancellationToken token)
    {
        if (await JoinAsync(RedisReplicaProof.VerifyWorkerPrimaryAsync(primary, topology, CancellationToken.None), token).ConfigureAwait(false) != replicationId)
        {
            throw new ComparisonFailureException(Failure);
        }
        RedisNodeIdentity.RequireUnchanged(await JoinAsync(RedisNodeIdentity.ReadAsync(primaryServer, CommandFlags.DemandMaster, CancellationToken.None), token).ConfigureAwait(false), primaryIdentity);
    }
    private static async Task<List<RedisNodeIdentity>> ObserveReplicasAsync(System.Net.EndPoint primaryEndpoint, string replicationId,
        RedisNodeIdentity primaryIdentity, string[] endpoints, List<ConnectionMultiplexer> replicas, CancellationToken token)
    {
        var identities = new List<RedisNodeIdentity>(endpoints.Length + PrimaryIdentityCount) { primaryIdentity };
        var endpointIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RedisProtocol.EndpointIdentity(primaryEndpoint) };
        foreach (var endpoint in endpoints)
        {
            token.ThrowIfCancellationRequested();
            var replica = await ConnectionMultiplexer.ConnectAsync(RedisReplicaProof.CreateOptions(endpoint)).ConfigureAwait(false);
            replicas.Add(replica);
            token.ThrowIfCancellationRequested();
            var actualEndpoint = RedisProtocol.ConfiguredEndpoint(replica, Failure);
            if (!endpointIds.Add(RedisProtocol.EndpointIdentity(actualEndpoint)))
            {
                throw new ComparisonFailureException(Failure);
            }

            var server = replica.GetServer(actualEndpoint);
            await RequireReplicaAsync(server, replicationId, token).ConfigureAwait(false);
            await RequireAofAsync(server, CommandFlags.DemandReplica, token).ConfigureAwait(false);
            identities.Add(await JoinAsync(RedisNodeIdentity.ReadAsync(server, CommandFlags.DemandReplica, CancellationToken.None), token).ConfigureAwait(false));
        }
        return identities;
    }
    private static async Task VerifyReplicaReadbacksAsync(List<ConnectionMultiplexer> replicas, List<RedisNodeIdentity> identities,
        string replicationId, DocumentComparisonSchedule schedule,
        Func<ConnectionMultiplexer, CancellationToken, IAsyncEnumerable<FoundDocument>> readReplica,
        List<string> observations, CancellationToken token)
    {
        for (var index = FirstReplicaIndex; index < replicas.Count; index++)
        {
            var readback = await DocumentComparisonOracle.VerifyAsync(readReplica(replicas[index], token), schedule.FinalDocuments(), token).ConfigureAwait(false);
            observations.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, FinalReplicaObservationFormat, identities[index + PrimaryIdentityCount].RunId, readback.Records, readback.ActualSha256));
            var server = replicas[index].GetServer(RedisProtocol.ConfiguredEndpoint(replicas[index], Failure));
            await RequireReplicaAsync(server, replicationId, token).ConfigureAwait(false);
            RedisNodeIdentity.RequireUnchanged(await JoinAsync(RedisNodeIdentity.ReadAsync(server, CommandFlags.DemandReplica, CancellationToken.None), token).ConfigureAwait(false), identities[index + PrimaryIdentityCount]);
        }
    }
    internal static void RequireInitialIdentities(ClusterEvidence initial, ClusterEvidence final)
    {
        var before = initial.Observations.Where(value => value.StartsWith(RedisNodeIdentity.ObservationPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        var after = final.Observations.Where(value => value.StartsWith(RedisNodeIdentity.ObservationPrefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal);
        if (initial.Nodes != final.Nodes || initial.DataCopies != final.DataCopies || !before.SequenceEqual(after, StringComparer.Ordinal))
        {
            throw new ComparisonFailureException(Failure);
        }
    }
    private static async Task RequireAofAsync(IServer server, CommandFlags flags, CancellationToken token)
    {
        var reply = (RedisResult[])(await JoinAsync(server.ExecuteAsync(ConfigCommand, new object[] { GetCommand, AofSetting, AofFlushSetting }, flags), token).ConfigureAwait(false))!;
        if (reply.Length % ConfigPairWidth != NoRemainder)
        {
            throw new ComparisonFailureException(Failure);
        }

        var values = Enumerable.Range(FirstConfigField, reply.Length / ConfigPairWidth).ToDictionary(index => reply[index * ConfigPairWidth].ToString(), index => reply[index * ConfigPairWidth + ConfigValueOffset].ToString(), StringComparer.Ordinal);
        if (values.GetValueOrDefault(AofSetting) != AofEnabled || values.GetValueOrDefault(AofFlushSetting) != AofAlways)
        {
            throw new ComparisonFailureException(Failure);
        }
    }
    // Replicas name the primary by its container-network address; the primary's replication id is the follow oracle.
    private static async Task RequireReplicaAsync(IServer server, string replicationId, CancellationToken token)
    {
        var info = await JoinAsync(RedisProtocol.ReadInfoAsync(server, RedisProtocol.ReplicationSection, CommandFlags.DemandReplica, CancellationToken.None), token).ConfigureAwait(false);
        var role = await JoinAsync(RedisProtocol.ReadRoleAsync(server, CommandFlags.DemandReplica, CancellationToken.None), token).ConfigureAwait(false);
        var host = info.GetValueOrDefault(RedisProtocol.MasterHostField);
        var port = RedisProtocol.ParseInteger(info, RedisProtocol.MasterPortField);
        if (info.GetValueOrDefault(RedisProtocol.RoleField) != RedisProtocol.ReplicaRole
            || info.GetValueOrDefault(RedisProtocol.LinkField) != RedisProtocol.LinkUp || host is null
            || !string.Equals(info.GetValueOrDefault(RedisProtocol.ReplicationIdField), replicationId, StringComparison.Ordinal)
            || role.Length < RedisProtocol.RoleReplicaResponseLength
            || role[RedisProtocol.RoleNameIndex].ToString() != RedisProtocol.ReplicaRole
            || !StringComparer.OrdinalIgnoreCase.Equals(role[RedisProtocol.RoleHostIndex].ToString(), host) || role[RedisProtocol.RolePortIndex].ToString() != port.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            throw new ComparisonFailureException(Failure);
        }
    }
    private static async Task<long> ClientIdAsync(IDatabase database, CancellationToken token)
        => (long)await JoinAsync(database.ExecuteAsync(ClientCommand, new object[] { ClientIdSubcommand }, CommandFlags.DemandMaster), token).ConfigureAwait(false);
    private static async Task RequireAllFsyncAsync(IDatabase database, int replicas, long clientId, TimeSpan timeout, CancellationToken token)
    {
        if (await ClientIdAsync(database, token).ConfigureAwait(false) != clientId)
        {
            throw new ComparisonFailureException(Failure);
        }

        var reply = (RedisResult[])(await JoinAsync(database.ExecuteAsync(WaitAofCommand, new object[] { RequiredLocalFsync, replicas, checked((int)Math.Ceiling(timeout.TotalMilliseconds)) }, CommandFlags.DemandMaster), token).ConfigureAwait(false))!;
        RequireFsyncReceipt(reply.Select(value => (long)value).ToArray(), replicas);
        if (await ClientIdAsync(database, token).ConfigureAwait(false) != clientId)
        {
            throw new ComparisonFailureException(Failure);
        }
    }
    internal static void RequireFsyncReceipt(long[] counts, int replicas)
    {
        if (counts.Length != ReceiptFieldCount || counts[LocalReceiptIndex] != RequiredLocalFsync || counts[ReplicaReceiptIndex] < replicas)
        {
            throw new ComparisonFailureException(Failure);
        }
    }

    private static async Task<T> JoinAsync<T>(Task<T> original, CancellationToken token)
    {
        var result = await original.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return result;
    }
}
