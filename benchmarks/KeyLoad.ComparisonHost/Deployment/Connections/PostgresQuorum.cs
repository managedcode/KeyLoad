using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.ComparisonHost.Deployment;

/// <summary>
/// Makes a three-node PostgreSQL deployment commit only after a synchronous <c>ANY 1</c> quorum of its two standbys
/// flushed the WAL, so every PostgreSQL cell acknowledges writes the same way.
/// </summary>
internal static class PostgresQuorum
{
    private const string QuorumSql = "ALTER SYSTEM SET synchronous_standby_names TO 'ANY 1 (\"benchmark_standby1\", \"benchmark_standby2\")'";
    private const string ReloadSql = "SELECT pg_reload_conf()";
    private const string StreamingSql = "SELECT count(*) FROM pg_stat_replication WHERE state = 'streaming' AND application_name = ANY($1)";
    private const string QuorumStateSql = StreamingSql + " AND sync_state = 'quorum'";
    private const string UnsupportedTopology = "The PostgreSQL deployment has one primary and either no or exactly two standbys.";
    private const string NotStreaming = "PostgreSQL standbys streaming: ";
    private const string NotQuorum = "PostgreSQL standbys in the synchronous quorum: ";
    private static readonly string[] StandbyNames = ["benchmark_standby1", "benchmark_standby2"];
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReadinessLimit = TimeSpan.FromMinutes(5);

    /// <summary>Does nothing on one node; on three nodes waits for both standbys and enables the synchronous quorum.</summary>
    internal static async Task RequireAsync(NpgsqlDataSource source, int nodes, CancellationToken token)
    {
        var standbys = nodes - 1;
        if (standbys == 0)
        {
            return;
        }
        if (standbys != StandbyNames.Length)
        {
            throw new NotSupportedException(UnsupportedTopology);
        }
        await using var session = await source.OpenConnectionAsync(token);
        await WaitForStandbysAsync(session, quorum: false, token);
        await using (var configure = new NpgsqlCommand(QuorumSql, session))
        {
            _ = await configure.ExecuteNonQueryAsync(token);
        }
        await using (var reload = new NpgsqlCommand(ReloadSql, session))
        {
            _ = await reload.ExecuteScalarAsync(token);
        }
        await WaitForStandbysAsync(session, quorum: true, token);
    }

    /// <summary>Polls every 2 s until every standby is observed; gives up after 5 minutes with the last count.</summary>
    private static async Task WaitForStandbysAsync(NpgsqlConnection session, bool quorum, CancellationToken token)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        var observed = await CountStandbysAsync(session, quorum, token);
        while (observed < StandbyNames.Length)
        {
            if (clock.GetElapsedTime(started) >= ReadinessLimit)
            {
                throw new TimeoutException((quorum ? NotQuorum : NotStreaming) + observed.ToString(CultureInfo.InvariantCulture));
            }
            await Task.Delay(RetryInterval, clock, token);
            observed = await CountStandbysAsync(session, quorum, token);
        }
    }

    private static async Task<long> CountStandbysAsync(NpgsqlConnection session, bool quorum, CancellationToken token)
    {
        await using var command = quorum ? new NpgsqlCommand(QuorumStateSql, session) : new NpgsqlCommand(StreamingSql, session);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, StandbyNames);
        return Convert.ToInt64(await command.ExecuteScalarAsync(token), CultureInfo.InvariantCulture);
    }
}
