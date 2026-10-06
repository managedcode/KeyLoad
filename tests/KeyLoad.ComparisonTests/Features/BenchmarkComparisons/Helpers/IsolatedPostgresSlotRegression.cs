using System.Globalization;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-002/003/006: native slots remain bound to actual copies across WAL recycling checkpoints.</summary>
internal static class IsolatedPostgresSlotRegression
{
    private const string Settings = "SELECT pg_size_bytes(current_setting('max_slot_wal_keep_size')), pg_is_in_recovery()";
    private const string Slots = "SELECT s.slot_name,s.slot_type,s.temporary,s.active,s.restart_lsn IS NOT NULL,s.wal_status,"
        + "r.application_name,r.state FROM pg_replication_slots s LEFT JOIN pg_stat_replication r ON r.pid=s.active_pid ORDER BY s.slot_name";
    private const string SwitchWal = "SELECT pg_switch_wal()::text";
    private const string Checkpoint = "CHECKPOINT";
    private const string CurrentWal = "SELECT pg_current_wal_lsn()::text";
    private const string Copies = "SELECT count(*) FROM pg_stat_replication WHERE state='streaming' "
        + "AND application_name IN ('benchmark_standby1','benchmark_standby2') "
        + "AND pg_wal_lsn_diff(flush_lsn,$1::pg_lsn)>=0 AND pg_wal_lsn_diff(replay_lsn,$1::pg_lsn)>=0";
    private const string Standby = "benchmark_standby";
    private const long RetentionBytes = 512L * 1024 * 1024;
    private const int ProbeMilliseconds = 200;

    internal static async Task VerifyAsync(string connectionString, int nodeCount, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await VerifySettingsAsync(connection, token);
        await VerifySlotsAsync(connection, nodeCount - 1, token);
        if (nodeCount == 1)
        {
            return;
        }

        await using var switchWal = new NpgsqlCommand(SwitchWal, connection);
        await switchWal.ExecuteScalarAsync(token);
        await using var checkpoint = new NpgsqlCommand(Checkpoint, connection);
        await checkpoint.ExecuteNonQueryAsync(token);
        await using var current = new NpgsqlCommand(CurrentWal, connection);
        var cut = (string)(await current.ExecuteScalarAsync(token))!;
        await WaitForCopiesAsync(connection, nodeCount - 1, cut, token);
        await VerifySlotsAsync(connection, nodeCount - 1, token);
    }

    private static async Task VerifySettingsAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(Settings, connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        await Assert.That(await reader.ReadAsync(token)).IsTrue();
        await Assert.That(reader.GetInt64(0)).IsEqualTo(RetentionBytes);
        await Assert.That(reader.GetBoolean(1)).IsFalse();
        await Assert.That(await reader.ReadAsync(token)).IsFalse();
    }

    private static async Task VerifySlotsAsync(NpgsqlConnection connection, int expected, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(Slots, connection);
        await using var reader = await command.ExecuteReaderAsync(token);
        var observed = 0;
        while (await reader.ReadAsync(token))
        {
            observed++;
            var name = Standby + observed.ToString(CultureInfo.InvariantCulture);
            await Assert.That(reader.GetString(0)).IsEqualTo(name);
            await Assert.That(reader.GetString(1)).IsEqualTo("physical");
            await Assert.That(reader.GetBoolean(2)).IsFalse();
            await Assert.That(reader.GetBoolean(3)).IsTrue();
            await Assert.That(reader.GetBoolean(4)).IsTrue();
            await Assert.That(reader.GetString(5) is "reserved" or "extended").IsTrue();
            await Assert.That(await reader.IsDBNullAsync(6, token)).IsFalse();
            await Assert.That(reader.GetString(6)).IsEqualTo(name);
            await Assert.That(reader.GetString(7)).IsEqualTo("streaming");
        }

        await Assert.That(observed).IsEqualTo(expected);
    }

    private static async Task WaitForCopiesAsync(NpgsqlConnection connection, int expected, string cut, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(NativeExecutionPolicyFixture.Harness().Value.PostgresSlotObservationTimeout);
        while (true)
        {
            await using var command = new NpgsqlCommand(Copies, connection);
            command.Parameters.AddWithValue(cut);
            var observed = (long)(await command.ExecuteScalarAsync(deadline.Token))!;
            if (observed == expected)
            {
                return;
            }

            await Task.Delay(ProbeMilliseconds, deadline.Token);
        }
    }
}
