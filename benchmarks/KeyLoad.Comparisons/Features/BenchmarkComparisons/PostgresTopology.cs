using Npgsql;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresTopology
{
    private const string ConfigureQuorum = "ALTER SYSTEM SET synchronous_standby_names TO 'ANY 1 (\"benchmark_standby1\", \"benchmark_standby2\")'";
    private const string ReloadConfiguration = "SELECT pg_reload_conf()";
    private const string ReplicationStatus = "SELECT application_name,state,sync_state,client_addr::text FROM pg_stat_replication ORDER BY application_name";
    private const string CurrentWal = "SELECT pg_current_wal_lsn()::text";
    private const string CopyStatus = "SELECT count(DISTINCT application_name) FROM pg_stat_replication WHERE state='streaming' AND sync_state='quorum' AND application_name IN ('benchmark_standby1','benchmark_standby2') AND pg_wal_lsn_diff(replay_lsn,$1::pg_lsn)>=0";
    private const string SingleStatus = "SELECT pg_is_in_recovery(), (SELECT count(*) FROM pg_stat_replication)";
    private const string QuorumConfiguration = "SHOW synchronous_standby_names";
    private const string ExpectedQuorumConfiguration = "ANY 1 (\"benchmark_standby1\", \"benchmark_standby2\")";
    private const string StandbyOne = "benchmark_standby1";
    private const string StandbyTwo = "benchmark_standby2";
    private const string Streaming = "streaming";
    private const string Quorum = "quorum";
    private const string SingleState = "single primary, no replicas";
    private const string ReplicatedState = "primary plus two streaming physical standbys; RF3 one shard; synchronous ANY 1 quorum";
    private const string ReplicaFailure = "PostgresReplicationReceiptMissing";

    internal static async Task ConfigureReplicationAsync(NpgsqlConnection connection, ComparisonTopology topology,
        CancellationToken cancellationToken)
    {
        if (topology != ComparisonTopology.Replicated)
        {
            return;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        await WaitForMembersAsync(connection, false, deadline.Token);
        await ConfigureQuorumAsync(connection, deadline.Token);
        await ReloadAsync(connection, deadline.Token);
        await WaitForMembersAsync(connection, true, deadline.Token);
    }

    internal static async Task<TargetProfile> ObserveCopiesAsync(NpgsqlConnection connection,
        ComparisonTopology topology, TargetProfile profile, CancellationToken cancellationToken)
    {
        if (topology == ComparisonTopology.Standalone)
        {
            await VerifySingleNodeAsync(connection, cancellationToken);
            return profile with { Cluster = new(1, 1, SingleState, [profile.WriteAcknowledgement]) };
        }

        return await ObserveReplicatedCopiesAsync(connection, profile, cancellationToken);
    }

    private static async Task ConfigureQuorumAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ConfigureQuorum, connection);
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

    private static async Task WaitForMembersAsync(NpgsqlConnection connection, bool requireQuorum,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var observation = await ReadMembersAsync(connection, requireQuorum, cancellationToken);
            if (HasExpectedMembers(observation))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
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

    private static bool HasExpectedMembers(ReplicaMembers observation)
        => observation.Observed == 2
            && observation.Names.SequenceEqual(new[] { StandbyOne, StandbyTwo }, StringComparer.Ordinal)
            && observation.Addresses.Distinct(StringComparer.Ordinal).Count() == 2;

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
        TargetProfile profile, CancellationToken cancellationToken)
    {
        await using var current = new NpgsqlCommand(CurrentWal, connection);
        var wal = (string)(await current.ExecuteScalarAsync(cancellationToken))!;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        await WaitForCopiesAsync(connection, wal, deadline.Token);
        await WaitForMembersAsync(connection, true, deadline.Token);
        await VerifyQuorumConfigurationAsync(connection, deadline.Token);
        return profile with
        {
            Topology = ReplicatedState,
            WriteAcknowledgement = "fsync=on; synchronous_commit=on; primary WAL flush plus one of two standby WAL flushes",
            Cluster = new(3, 3, ReplicatedState, [$"Both named standbys replayed seeded corpus through WAL {wal}", "Observed two streaming quorum members; this does not acknowledge all three copies on every write"])
        };
    }

    private static async Task VerifyQuorumConfigurationAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(QuorumConfiguration, connection);
        if (!Equals(await command.ExecuteScalarAsync(cancellationToken), ExpectedQuorumConfiguration))
        {
            throw new ComparisonFailureException(ReplicaFailure);
        }
    }

    private static async Task WaitForCopiesAsync(NpgsqlConnection connection, string wal,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var command = new NpgsqlCommand(CopyStatus, connection);
            command.Parameters.AddWithValue(wal);
            if (Equals(await command.ExecuteScalarAsync(cancellationToken), 2L))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }

    private sealed class ReplicaMembers
    {
        internal int Observed { get; set; }
        internal List<string> Names { get; } = [];
        internal List<string> Addresses { get; } = [];

        internal void Add(ReplicaMember member)
        {
            Names.Add(member.Name);
            if (member.Address is not null)
            {
                Addresses.Add(member.Address);
            }
        }
    }

    private sealed record ReplicaMember(string Name, string? Address);
}
