using System.Security.Cryptography;
using System.Text;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries;

internal static class TimeSeriesForeignSchemaCollision
{
    private const string SchemaPrefix = "keyload_tsc_";
    private const string SetSchemaSql = "SELECT set_config('keyload.timeseries_schema', $1, false)";
    private const string SetSearchPathSql = "SELECT set_config('search_path', $1, false)";
    private const string CreateSchemaSql = "DO $$ BEGIN EXECUTE format('CREATE SCHEMA %I', " +
        "current_setting('keyload.timeseries_schema')); END $$";
    private const string CreateMarkerSql = "CREATE TABLE owner_marker (owner_id uuid PRIMARY KEY)";
    private const string InsertMarkerSql = "INSERT INTO owner_marker (owner_id) VALUES ($1)";
    private const string ReadMarkerSql = "SELECT owner_id FROM owner_marker";
    private const string SchemaExistsSql = "SELECT EXISTS(SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname=$1)";
    private const string DropSchemaSql = "DO $$ BEGIN EXECUTE format('DROP SCHEMA IF EXISTS %I CASCADE', " +
        "current_setting('keyload.timeseries_schema')); END $$";

    internal static async Task VerifyAsync(string connectionString, CancellationToken cancellationToken)
    {
        var runId = "foreign-owner-" + Guid.NewGuid().ToString("N");
        var schema = SchemaName(runId);
        var ownerId = Guid.NewGuid();
        var testSchemaCreated = false;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        try
        {
            await CreateSchemaAsync(connection, schema, cancellationToken);
            testSchemaCreated = true;
            await InstallForeignMarkerAsync(connection, schema, ownerId, cancellationToken);
            await VerifyRejectedInitializationAsync(connectionString, runId, cancellationToken);
            await VerifyForeignMarkerAsync(connection, schema, ownerId, cancellationToken);
        }
        finally
        {
            if (testSchemaCreated)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15), TimeProvider.System);
                await DropTestSchemaAsync(connection, schema, cleanup.Token);
            }
        }
    }

    private static async Task CreateSchemaAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        await SetOwnedSchemaSettingAsync(connection, schema, cancellationToken);
        await using var create = new NpgsqlCommand(CreateSchemaSql, connection);
        await create.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InstallForeignMarkerAsync(NpgsqlConnection connection, string schema, Guid ownerId,
        CancellationToken cancellationToken)
    {
        await SetSearchPathAsync(connection, schema, cancellationToken);
        await using var table = new NpgsqlCommand(CreateMarkerSql, connection);
        await table.ExecuteNonQueryAsync(cancellationToken);
        await using var marker = new NpgsqlCommand(InsertMarkerSql, connection);
        marker.Parameters.AddWithValue(ownerId);
        await marker.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task VerifyRejectedInitializationAsync(string connectionString, string runId,
        CancellationToken cancellationToken)
    {
        var workload = TimeSeriesComparisonWorkloadFactory.Create(runId);
        await using var target = new TimescaleTimeSeriesTarget(connectionString, NativeExecutionPolicyFixture.Lifecycle());
        InvalidOperationException? failure = null;
        try
        {
            await target.InitializeAsync(workload, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            failure = exception;
        }

        await target.DisposeAsync();
        await Assert.That(failure).IsNotNull();
        await Assert.That(TimeSeriesComparisonTargetErrors.TryGetCode(failure!, out var errorCode)).IsTrue();
        await Assert.That(errorCode).IsEqualTo("TimescaleInitializeFailed");
    }

    private static async Task VerifyForeignMarkerAsync(NpgsqlConnection connection, string schema, Guid ownerId,
        CancellationToken cancellationToken)
    {
        await SetSearchPathAsync(connection, schema, cancellationToken);
        await using var command = new NpgsqlCommand(ReadMarkerSql, connection);
        var actual = await command.ExecuteScalarAsync(cancellationToken);
        await Assert.That(actual).IsEqualTo(ownerId);
        await Assert.That(await SchemaExistsAsync(connection, schema, cancellationToken)).IsTrue();
    }

    private static async Task<bool> SchemaExistsAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SchemaExistsSql, connection);
        command.Parameters.AddWithValue(schema);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task DropTestSchemaAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        await SetOwnedSchemaSettingAsync(connection, schema, cancellationToken);
        await using var command = new NpgsqlCommand(DropSchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetOwnedSchemaSettingAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SetSchemaSql, connection);
        command.Parameters.AddWithValue(schema);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task SetSearchPathAsync(NpgsqlConnection connection, string schema,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SetSearchPathSql, connection);
        command.Parameters.AddWithValue(schema);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static string SchemaName(string runId)
        => SchemaPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(runId)).AsSpan(0, 16));
}
