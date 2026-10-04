using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class PostgresSchemaContextReset
{
    private const string SetContextSql = "SELECT pg_catalog.set_config('keyload.benchmark.run_id', $1::uuid::text, true), pg_catalog.set_config('keyload.benchmark.owner_id', $2::uuid::text, true), pg_catalog.set_config('keyload.benchmark.dimensions', $3::integer::text, true), pg_catalog.set_config('keyload.benchmark.lock_key', $4::bigint::text, true)";
    private const string ReadContextSql = "SELECT current_setting('keyload.benchmark.run_id', true), current_setting('keyload.benchmark.owner_id', true), current_setting('keyload.benchmark.dimensions', true), current_setting('keyload.benchmark.lock_key', true)";
    private const string BackendPidSql = "SELECT pg_catalog.pg_backend_pid()";

    // This proves PostgreSQL's real transaction-local settings and Npgsql pool reset semantics.
    // Source review separately verifies that PostgresTarget passes is_local=true for these four keys;
    // this helper does not inspect target internals or claim that it observed the target's data source.
    internal static async Task VerifyAsync(string connectionString, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var poolConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MaxPoolSize = 1,
            MinPoolSize = 1,
            Timeout = 5
        }.ConnectionString;
        await using var source = NpgsqlDataSource.Create(poolConnectionString);
        var runId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var lockKey = PostgresSchemaSupport.LockKey(runId);
        var values = new ContextValues(runId, ownerId, 17, lockKey);
        var backendPid = await VerifyCommittedContextAsync(source, values, timeout.Token);
        await VerifyReopenedPoolAsync(source, values, backendPid, timeout.Token);
        await VerifyRolledBackContextAsync(source, values, backendPid, timeout.Token);
    }

    private static async Task<int> VerifyCommittedContextAsync(NpgsqlDataSource source, ContextValues values,
        CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        var backendPid = await BackendPidAsync(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetContextAsync(connection, transaction, values, cancellationToken);
        await Assert.That(await ContextMatchesAsync(connection, transaction, values, cancellationToken)).IsTrue();
        await transaction.CommitAsync(cancellationToken);
        await Assert.That(await ContextIsEmptyAsync(connection, cancellationToken)).IsTrue();
        return backendPid;
    }

    private static async Task VerifyReopenedPoolAsync(NpgsqlDataSource source, ContextValues values, int expectedBackendPid,
        CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await Assert.That(await BackendPidAsync(connection, cancellationToken)).IsEqualTo(expectedBackendPid);
        await Assert.That(await ContextIsEmptyAsync(connection, cancellationToken)).IsTrue();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetContextAsync(connection, transaction, values, cancellationToken);
        await Assert.That(await ContextMatchesAsync(connection, transaction, values, cancellationToken)).IsTrue();
        await transaction.RollbackAsync(cancellationToken);
        await Assert.That(await ContextIsEmptyAsync(connection, cancellationToken)).IsTrue();
    }

    private static async Task VerifyRolledBackContextAsync(NpgsqlDataSource source, ContextValues values,
        int expectedBackendPid, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await Assert.That(await BackendPidAsync(connection, cancellationToken)).IsEqualTo(expectedBackendPid);
        await Assert.That(await ContextIsEmptyAsync(connection, cancellationToken)).IsTrue();
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetContextAsync(connection, transaction, values, cancellationToken);
        await Assert.That(await ContextMatchesAsync(connection, transaction, values, cancellationToken)).IsTrue();
        await transaction.RollbackAsync(cancellationToken);
        await Assert.That(await ContextIsEmptyAsync(connection, cancellationToken)).IsTrue();
    }

    private static async Task SetContextAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ContextValues values, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SetContextSql, connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, values.RunId);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, values.OwnerId);
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, values.Dimensions);
        command.Parameters.AddWithValue(NpgsqlDbType.Bigint, values.LockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> ContextMatchesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        ContextValues values, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadContextSql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return reader.GetString(0) == values.RunId.ToString("D")
            && reader.GetString(1) == values.OwnerId.ToString("D")
            && reader.GetString(2) == values.Dimensions.ToString(CultureInfo.InvariantCulture)
            && reader.GetString(3) == values.LockKey.ToString(CultureInfo.InvariantCulture);
    }

    private static async Task<bool> ContextIsEmptyAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReadContextSql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return IsEmpty(reader, 0) && IsEmpty(reader, 1) && IsEmpty(reader, 2) && IsEmpty(reader, 3);
    }

    private static bool IsEmpty(NpgsqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) || reader.GetString(ordinal).Length == 0;

    private static async Task<int> BackendPidAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BackendPidSql, connection);
        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private sealed record ContextValues(Guid RunId, Guid OwnerId, int Dimensions, long LockKey);
}
