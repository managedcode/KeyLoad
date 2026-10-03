using System.Runtime.ExceptionServices;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal static class TimescaleTimeSeriesIntensiveSchemaAtomicityRegression
{
    private const string SchemaExistsSql = "SELECT EXISTS(SELECT 1 FROM pg_catalog.pg_namespace WHERE nspname = $1)";
    private const string ExtensionExistsSql = "SELECT EXISTS(SELECT 1 FROM pg_catalog.pg_extension WHERE extname = 'timescaledb')";
    private const string HealthySuffix = "-healthy";
    private const string CurrentRoleSql = "SELECT current_user";

    /// <summary>AC-TSI-003: genuine native installer denial must roll back CREATE SCHEMA.</summary>
    internal static async Task VerifyAsync(string connectionString, CancellationToken token)
    {
        await using var fixture = await TimescaleTimeSeriesIntensiveSchemaFailureFixture.CreateAsync(connectionString, token)
            .ConfigureAwait(false);
        try
        {
            await VerifyNativeDenialAsync(fixture, token).ConfigureAwait(false);
            await VerifyHealthyFollowupAsync(fixture, token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            fixture.Primary = ExceptionDispatchInfo.Capture(error);
            throw;
        }
    }

    private static async Task VerifyNativeDenialAsync(TimescaleTimeSeriesIntensiveSchemaFailureFixture fixture,
        CancellationToken token)
    {
        await using var observer = new NpgsqlConnection(fixture.PrivilegedConnectionString);
        await observer.OpenAsync(token).ConfigureAwait(false);
        await using (var extension = new NpgsqlCommand(ExtensionExistsSql, observer))
        {
            await Assert.That(await extension.ExecuteScalarAsync(token).ConfigureAwait(false) is false).IsTrue();
        }
        await using (var restricted = new NpgsqlConnection(fixture.RestrictedConnectionString))
        {
            await restricted.OpenAsync(token).ConfigureAwait(false);
            await using var role = new NpgsqlCommand(CurrentRoleSql, restricted);
            await Assert.That(await role.ExecuteScalarAsync(token).ConfigureAwait(false)).IsEqualTo(TimescaleTimeSeriesIntensiveSchemaFailureFixture.RestrictedRole);
        }
        var schema = TimescaleSchemaLifecycle.SchemaName(fixture.RunId);
        var ownerId = Guid.NewGuid();
        var denial = await CaptureNativeDenialAsync(fixture.RestrictedConnectionString, schema, ownerId, token)
            .ConfigureAwait(false);
        await Assert.That(denial).IsNotNull();
        await Assert.That(denial!.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        await Assert.That(await ExistsAsync(observer, schema, token).ConfigureAwait(false)).IsFalse();
    }

    private static async Task VerifyHealthyFollowupAsync(TimescaleTimeSeriesIntensiveSchemaFailureFixture fixture,
        CancellationToken token)
    {
        var schema = TimescaleSchemaLifecycle.SchemaName(fixture.RunId + HealthySuffix);
        var ownerId = Guid.NewGuid();
        var confirmed = await TimescaleSchemaLifecycle.InitializeAsync(fixture.PrivilegedConnectionString,
            schema, ownerId, token).ConfigureAwait(false);
        await Assert.That(confirmed).IsTrue();
        await TimescaleSchemaLifecycle.DropOwnedSchemaAsync(fixture.PrivilegedConnectionString,
            schema, ownerId, token).ConfigureAwait(false);
        await using var observer = new NpgsqlConnection(fixture.PrivilegedConnectionString);
        await observer.OpenAsync(token).ConfigureAwait(false);
        await Assert.That(await ExistsAsync(observer, schema, token).ConfigureAwait(false)).IsFalse();
    }

    private static async Task<bool> ExistsAsync(NpgsqlConnection observer, string schema, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(SchemaExistsSql, observer);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, schema);
        return await command.ExecuteScalarAsync(token).ConfigureAwait(false) is true;
    }

    private static async Task<PostgresException?> CaptureNativeDenialAsync(string connectionString,
        string schema, Guid ownerId, CancellationToken token)
    {
        try
        {
            await TimescaleSchemaLifecycle.InitializeAsync(connectionString, schema, ownerId, token)
                .ConfigureAwait(false);
            return null;
        }
        catch (PostgresException error)
        {
            return error;
        }
    }
}
