using System.Security.Cryptography;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimescaleSchemaLifecycle
{
    private const string SchemaPrefix = "keyload_tsc_";
    private const string SetSchemaSettingSql = "SELECT set_config('keyload.timeseries_schema', $1, false)";
    private const string SetSearchPathSql = "SELECT set_config('search_path', $1, false)";
    private const string CreateSchemaSql = "DO $$ BEGIN EXECUTE format('CREATE SCHEMA %I', " +
        "current_setting('keyload.timeseries_schema')); END $$";
    private const string CreateExtensionSql = "CREATE EXTENSION IF NOT EXISTS timescaledb WITH SCHEMA public";
    private const string CreateMarkerSql = "CREATE TABLE owner_marker (owner_id uuid PRIMARY KEY)";
    private const string InsertMarkerSql = "INSERT INTO owner_marker (owner_id) VALUES ($1)";
    private const string CreateSamplesSql = "CREATE TABLE samples (sample_time timestamptz NOT NULL, " +
        "event_id text NOT NULL, set_name text NOT NULL, series_id text NOT NULL, " +
        "sample_value double precision NOT NULL, sample_sequence bigint NOT NULL, tags_json jsonb NOT NULL, " +
        "PRIMARY KEY (sample_time, event_id)) WITH (tsdb.hypertable, tsdb.partition_column='sample_time')";
    private const string VerifyMarkerSql = "SELECT count(*) = 1 AND bool_and(owner_id = $1) FROM owner_marker";
    private const string LockMarkerSql = "LOCK TABLE owner_marker IN ACCESS EXCLUSIVE MODE";
    private const string DropSchemaSql = "DO $$ BEGIN EXECUTE format('DROP SCHEMA %I CASCADE', " +
        "current_setting('keyload.timeseries_schema')); END $$";

    internal static string SchemaName(string runId)
        => SchemaPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(runId)).AsSpan(0, 16));

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

    internal static async Task<bool> InitializeAsync(string connectionString, string schemaName, Guid ownerId,
        CancellationToken cancellationToken)
    {
        ValidateSchemaName(schemaName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureSchemaAsync(connection, schemaName, cancellationToken).ConfigureAwait(false);
        await using (var createSchema = new NpgsqlCommand(CreateSchemaSql, connection))
        {
            await createSchema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await SetSearchPathAsync(connection, schemaName, cancellationToken).ConfigureAwait(false);
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
        {
            await using (var extension = new NpgsqlCommand(CreateExtensionSql, connection, transaction))
            {
                await extension.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var markerTable = new NpgsqlCommand(CreateMarkerSql, connection, transaction))
            {
                await markerTable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await using (var marker = new NpgsqlCommand(InsertMarkerSql, connection, transaction))
            {
                marker.Parameters.AddWithValue(NpgsqlDbType.Uuid, ownerId);
                await marker.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var samplesTable = new NpgsqlCommand(CreateSamplesSql, connection, transaction))
            {
                await samplesTable.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return await MarkerMatchesAsync(connection, ownerId, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task DropOwnedSchemaAsync(string connectionString, string schemaName, Guid ownerId,
        CancellationToken cancellationToken)
    {
        ValidateSchemaName(schemaName);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureSchemaAsync(connection, schemaName, cancellationToken).ConfigureAwait(false);
        await SetSearchPathAsync(connection, schemaName, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using (var lockMarker = new NpgsqlCommand(LockMarkerSql, connection, transaction))
        {
            await lockMarker.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        if (!await MarkerMatchesAsync(connection, ownerId, cancellationToken, transaction).ConfigureAwait(false))
        {
            throw TimeSeriesComparisonTargetErrors.Create("TimescaleOwnerMarkerMismatch");
        }

        await using (var dropSchema = new NpgsqlCommand(DropSchemaSql, connection, transaction))
        {
            await dropSchema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ConfigureSchemaAsync(NpgsqlConnection connection, string schemaName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SetSchemaSettingSql, connection);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, schemaName);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task SetSearchPathAsync(NpgsqlConnection connection, string schemaName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SetSearchPathSql, connection);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, schemaName);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> MarkerMatchesAsync(NpgsqlConnection connection, Guid ownerId,
        CancellationToken cancellationToken, NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(VerifyMarkerSql, connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, ownerId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is true;
    }

    private static void ValidateSchemaName(string schemaName)
    {
        ArgumentNullException.ThrowIfNull(schemaName);
        if (schemaName.Length != SchemaPrefix.Length + 32 ||
            !schemaName.StartsWith(SchemaPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The benchmark schema name is invalid.", nameof(schemaName));
        }

        foreach (var character in schemaName.AsSpan(SchemaPrefix.Length))
        {
            if (!char.IsAsciiDigit(character) && character is not (>= 'a' and <= 'f'))
            {
                throw new ArgumentException("The benchmark schema name is invalid.", nameof(schemaName));
            }
        }
    }

}
