using Npgsql;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresTopology
{
    private const int ReadinessTimeoutSeconds = 60;
    private const int ReadinessPollMilliseconds = 200;

    private const string ConfigureQuorum = "ALTER SYSTEM SET synchronous_standby_names TO 'ANY 1 (\"benchmark_standby1\", \"benchmark_standby2\")'";
    private const string ConfigureTwoNodeQuorum = "ALTER SYSTEM SET synchronous_standby_names TO 'ANY 1 (\"benchmark_standby1\")'";
    private const string ReloadConfiguration = "SELECT pg_reload_conf()";
    private const string ReplicationStatus = "SELECT application_name,state,sync_state,client_addr::text FROM pg_stat_replication ORDER BY application_name";
    private const string CurrentWal = "SELECT pg_current_wal_lsn()::text";
    private const string CopyStatus = "SELECT count(DISTINCT application_name) FROM pg_stat_replication WHERE state='streaming' AND sync_state='quorum' "
        + "AND application_name IN ('benchmark_standby1','benchmark_standby2') AND pg_wal_lsn_diff(replay_lsn,$1::pg_lsn)>=0 AND pg_wal_lsn_diff(flush_lsn,$1::pg_lsn)>=0";
    private const string SingleStatus = "SELECT pg_is_in_recovery(), (SELECT count(*) FROM pg_stat_replication)";
    private const string QuorumConfiguration = "SHOW synchronous_standby_names";
    private const string ExpectedQuorumConfiguration = "ANY 1 (\"benchmark_standby1\", \"benchmark_standby2\")";
    private const string ExpectedTwoNodeQuorumConfiguration = "ANY 1 (\"benchmark_standby1\")";
    private const string StandbyOne = "benchmark_standby1";
    private const string StandbyTwo = "benchmark_standby2";
    private const string Streaming = "streaming";
    private const string Quorum = "quorum";
    private const string SingleState = "single primary, no replicas";
    private const string ReplicatedState = "primary plus two streaming physical standbys; RF3 one shard; synchronous ANY 1 quorum";
    private const string ReplicaFailure = "PostgresReplicationReceiptMissing";
    private const string TwoNodeState = "primary plus one streaming physical standby; synchronous ANY 1 quorum";
    private const string TwoNodeAcknowledgement = "fsync=on; synchronous_commit=on; primary and sole standby WAL flush";
    private const string ReplicatedAcknowledgement = "fsync=on; synchronous_commit=on; primary WAL flush plus one of two standby WAL flushes";
    private const string CopyObservation = "Named standbys flushed and replayed seeded corpus through WAL ";
    private const string QuorumObservation = "Native ANY 1 waits for one standby WAL flush; untimed copy receipt covers every configured standby";
    private const string MemberObservation = "Native standby identities=";
    private const string IdentitySeparator = "@";

    internal static async Task ConfigureReplicationAsync(NpgsqlConnection connection, ComparisonTopology topology,
        CancellationToken cancellationToken)
    {
        if (ComparisonTopologies.NodeCount(topology) == 1)
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(ReadinessTimeoutSeconds));
        await WaitForMembersAsync(connection, topology, false, deadline.Token);
        await ConfigureQuorumAsync(connection, topology, deadline.Token);
        await ReloadAsync(connection, deadline.Token);
        await WaitForMembersAsync(connection, topology, true, deadline.Token);
    }

    internal static async Task<TargetProfile> ObserveCopiesAsync(NpgsqlConnection connection,
        ComparisonTopology topology, TargetProfile profile, CancellationToken cancellationToken)
    {
        if (topology == ComparisonTopology.Standalone)
        {
            await VerifySingleNodeAsync(connection, cancellationToken);
            return profile with { Cluster = new(1, 1, SingleState, [profile.WriteAcknowledgement]) };
        }

        return await ObserveReplicatedCopiesAsync(connection, topology, profile, cancellationToken);
    }

    private static async Task ConfigureQuorumAsync(NpgsqlConnection connection, ComparisonTopology topology, CancellationToken cancellationToken)
    {
        await using var command = topology == ComparisonTopology.TwoNode
            ? new NpgsqlCommand(ConfigureTwoNodeQuorum, connection)
            : new NpgsqlCommand(ConfigureQuorum, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReloadAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReloadConfiguration, connection);
        if (!Equals(await command.ExecuteScalarAsync(cancellationToken), true))
        {
            throw new ComparisonFailureException(ReplicaFailure);
        }
    }

    private static async Task<ReplicaMembers> WaitForMembersAsync(NpgsqlConnection connection, ComparisonTopology topology, bool requireQuorum,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var observation = await ReadMembersAsync(connection, requireQuorum, cancellationToken);
            if (observation.Observed == ComparisonTopologies.NodeCount(topology) - 1 &&
                HasExpectedMembers(topology, observation.Members.Select(member => (member.Name, member.Address)).ToArray()))
            {
                return observation;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(ReadinessPollMilliseconds), cancellationToken);
        }
    }

    private static async Task<ReplicaMembers> ReadMembersAsync(NpgsqlConnection connection, bool requireQuorum,
        CancellationToken cancellationToken)
    {
        var observation = new ReplicaMembers();
        await using var command = new NpgsqlCommand(ReplicationStatus, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            observation.Observed++;
            var member = await ReadMemberAsync(reader, requireQuorum, cancellationToken);
            if (member is not null)
            {
                observation.Add(member);
            }
        }

        return observation;
    }

    private static async Task<ReplicaMember?> ReadMemberAsync(NpgsqlDataReader reader, bool requireQuorum,
        CancellationToken cancellationToken)
    {
        if (reader.GetString(1) != Streaming || requireQuorum && reader.GetString(2) != Quorum)
        {
            return null;
        }

        var address = await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3);
        return new(reader.GetString(0), address);
    }

    internal static string QuorumSettings(ComparisonTopology topology) => topology switch
    {
        ComparisonTopology.TwoNode => ExpectedTwoNodeQuorumConfiguration,
        ComparisonTopology.Replicated => ExpectedQuorumConfiguration,
        _ => throw new ArgumentOutOfRangeException(nameof(topology)),
    };

    internal static bool HasExpectedMembers(ComparisonTopology topology, (string Name, string? Address)[] members)
    {
        var count = ComparisonTopologies.NodeCount(topology) - 1;
        var names = count == 1 ? new[] { StandbyOne } : new[] { StandbyOne, StandbyTwo };
        return members.Length == count && members.Select(member => member.Name).Order(StringComparer.Ordinal).SequenceEqual(names, StringComparer.Ordinal)
            && members.All(member => !string.IsNullOrWhiteSpace(member.Address))
            && members.Select(member => member.Address).Distinct(StringComparer.Ordinal).Count() == count;
    }

    private static async Task VerifySingleNodeAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var verify = new NpgsqlCommand(SingleStatus, connection);
        await using var reader = await verify.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.GetBoolean(0) || reader.GetInt64(1) != 0)
        {
            throw new ComparisonFailureException(ReplicaFailure);
        }
    }

    private static async Task<TargetProfile> ObserveReplicatedCopiesAsync(NpgsqlConnection connection,
        ComparisonTopology topology, TargetProfile profile, CancellationToken cancellationToken)
    {
        await using var current = new NpgsqlCommand(CurrentWal, connection);
        var wal = (string)(await current.ExecuteScalarAsync(cancellationToken))!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(ReadinessTimeoutSeconds));
        await WaitForCopiesAsync(connection, topology, wal, deadline.Token);
        var members = await WaitForMembersAsync(connection, topology, true, deadline.Token);
        await VerifyQuorumConfigurationAsync(connection, topology, deadline.Token);
        var nodes = ComparisonTopologies.NodeCount(topology);
        var state = topology == ComparisonTopology.TwoNode ? TwoNodeState : ReplicatedState;
        return profile with
        {
            Topology = state,
            WriteAcknowledgement = topology == ComparisonTopology.TwoNode ? TwoNodeAcknowledgement : ReplicatedAcknowledgement,
            Cluster = new(nodes, nodes, state, [CopyObservation + wal, QuorumObservation,
                MemberObservation + string.Join(',', members.Members.Select(member => member.Name + IdentitySeparator + member.Address))])
        };
    }

    private static async Task VerifyQuorumConfigurationAsync(NpgsqlConnection connection,
        ComparisonTopology topology, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(QuorumConfiguration, connection);
        if (!Equals(await command.ExecuteScalarAsync(cancellationToken), QuorumSettings(topology)))
        {
            throw new ComparisonFailureException(ReplicaFailure);
        }
    }

    private static async Task WaitForCopiesAsync(NpgsqlConnection connection, ComparisonTopology topology, string wal,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var command = new NpgsqlCommand(CopyStatus, connection);
            command.Parameters.AddWithValue(wal);
            if (Equals(await command.ExecuteScalarAsync(cancellationToken), (long)ComparisonTopologies.NodeCount(topology) - 1))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(ReadinessPollMilliseconds), cancellationToken);
        }
    }

    private sealed class ReplicaMembers
    {
        internal int Observed { get; set; }
        internal List<ReplicaMember> Members { get; } = [];

        internal void Add(ReplicaMember member)
        {
            Members.Add(member);
        }
    }

    private sealed record ReplicaMember(string Name, string? Address);
}
