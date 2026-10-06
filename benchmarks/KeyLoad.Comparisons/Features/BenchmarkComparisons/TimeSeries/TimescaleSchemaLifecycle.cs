using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimescaleSchemaLifecycle
{
    private const int FirstElementIndex = 0;
    private const int GuidDigestBytes = 16;

    private const string SchemaPrefix = "keyload_tsc_";
    private const string SetSchemaSettingSql = "SELECT set_config('keyload.timeseries_schema', $1, false)";
    private const string SetSearchPathSql = "SELECT set_config('search_path', $1, false)";
    internal const string CreateSchemaSql = "DO $$ BEGIN EXECUTE format('CREATE SCHEMA %I', " +
        "current_setting('keyload.timeseries_schema')); END $$";
    internal const string CreateExtensionSql = "CREATE EXTENSION IF NOT EXISTS timescaledb WITH SCHEMA public";
    internal const string CreateMarkerSql = "CREATE TABLE owner_marker (owner_id uuid PRIMARY KEY)";
    internal const string InsertMarkerSql = "INSERT INTO owner_marker (owner_id) VALUES ($1)";
    private const string CreateSamplesSql = "CREATE TABLE samples (sample_time timestamptz NOT NULL, " +
        "event_id text NOT NULL, set_name text NOT NULL, series_id text NOT NULL, " +
        "sample_value double precision NOT NULL, sample_sequence bigint NOT NULL, tags_json jsonb NOT NULL, " +
        "PRIMARY KEY (sample_time, event_id)) WITH (tsdb.hypertable, tsdb.partition_column='sample_time')";
    private const string VerifyMarkerSql = "SELECT count(*) = 1 AND bool_and(owner_id = $1) FROM owner_marker";
    internal const string LockMarkerSql = "LOCK TABLE owner_marker IN ACCESS EXCLUSIVE MODE";
    internal const string DropSchemaSql = "DO $$ BEGIN EXECUTE format('DROP SCHEMA %I CASCADE', " +
        "current_setting('keyload.timeseries_schema')); END $$";

    internal static string SchemaName(string runId)
        => SchemaPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(runId)).AsSpan(FirstElementIndex, GuidDigestBytes));

    internal static async Task<NpgsqlConnection> OpenConfiguredAsync(string connectionString, string schemaName,
        CancellationToken cancellationToken)
    {
        ValidateSchemaName(schemaName);
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await SetSearchPathAsync(connection, schemaName, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch (OperationCanceledException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (NpgsqlException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (ArgumentException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (InvalidOperationException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (TimeoutException)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal static Task<bool> InitializeAsync(string connectionString, string schemaName, Guid ownerId,
        CancellationToken cancellationToken)
        => InitializeOwnedAsync(connectionString, schemaName, ownerId, InstallLegacyAsync, cancellationToken);

    internal static Task<bool> InitializeOwnedAsync(string connectionString, string schemaName, Guid ownerId,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> installer, CancellationToken token)
    {
        ValidateSchemaName(schemaName);
        ArgumentNullException.ThrowIfNull(installer);
        return TimescaleSchemaConnection.ExecuteAsync(connectionString, null, connection =>
            TimescaleSchemaInstallation.InitializeAsync(connection, schemaName, ownerId, installer, token));
    }

    internal static Task<bool> InitializeOwnedAsync(NpgsqlDataSource dataSource, string schemaName, Guid ownerId,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> installer, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ValidateSchemaName(schemaName);
        ArgumentNullException.ThrowIfNull(installer);
        return TimescaleSchemaConnection.ExecuteAsync(null, dataSource, connection =>
            TimescaleSchemaInstallation.InitializeAsync(connection, schemaName, ownerId, installer, token));
    }

    internal static Task DropOwnedSchemaAsync(string connectionString, string schemaName, Guid ownerId,
        CancellationToken cancellationToken)
    {
        ValidateSchemaName(schemaName);
        return TimescaleSchemaConnection.ExecuteAsync(connectionString, null, async connection =>
        {
            await TimescaleSchemaInstallation.DropAsync(connection, schemaName, ownerId, cancellationToken).ConfigureAwait(false);
            return true;
        });
    }

    internal static Task DropOwnedSchemaAsync(NpgsqlDataSource dataSource, string schemaName, Guid ownerId,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ValidateSchemaName(schemaName);
        return TimescaleSchemaConnection.ExecuteAsync(null, dataSource, async connection =>
        {
            await TimescaleSchemaInstallation.DropAsync(connection, schemaName, ownerId, token).ConfigureAwait(false);
            return true;
        });
    }

    internal static async Task ConfigureSchemaAsync(NpgsqlConnection connection, string schemaName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SetSchemaSettingSql, connection);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, schemaName);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task SetSearchPathAsync(NpgsqlConnection connection, string schemaName,
        CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(SetSearchPathSql, connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, schemaName);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<bool> MarkerMatchesAsync(NpgsqlConnection connection, Guid ownerId,
        CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(VerifyMarkerSql, connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, ownerId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    internal static void ValidateSchemaName(string schemaName)
    {
        const int HexadecimalSchemaDigestCharacters = 32;
        const string BenchmarkSchemaNameIsInvalidDetail = "The benchmark schema name is invalid.";
        const char LowerHexadecimalStart = 'a';
        const char LowerHexadecimalEnd = 'f';

        ArgumentNullException.ThrowIfNull(schemaName);
        if (schemaName.Length != SchemaPrefix.Length + HexadecimalSchemaDigestCharacters ||
            !schemaName.StartsWith(SchemaPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(BenchmarkSchemaNameIsInvalidDetail, nameof(schemaName));
        }

        foreach (var character in schemaName.AsSpan(SchemaPrefix.Length))
        {
            if (!char.IsAsciiDigit(character) && character is not (>= LowerHexadecimalStart and <= LowerHexadecimalEnd))
            {
                throw new ArgumentException(BenchmarkSchemaNameIsInvalidDetail, nameof(schemaName));
            }
        }
    }

    private static async Task InstallLegacyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CreateSamplesSql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

}
