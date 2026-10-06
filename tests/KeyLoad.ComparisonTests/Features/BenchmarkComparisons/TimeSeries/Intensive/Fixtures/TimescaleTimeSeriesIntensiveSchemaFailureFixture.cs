using System.Runtime.ExceptionServices;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimescaleTimeSeriesIntensiveSchemaFailureFixture : IAsyncDisposable
{
    private const string AdminDatabase = "postgres";
    private const string DatabaseName = "keyload_tsi_ddl_fixture";
    private const string RoleName = "keyload_tsi_role_fixture";
    private const string GuidFormat = "N";
    private const string CreateRoleSql = "CREATE ROLE keyload_tsi_role_fixture NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION";
    private const string CreateDatabaseSql = "CREATE DATABASE keyload_tsi_ddl_fixture OWNER keyload_tsi_role_fixture TEMPLATE template0";
    private const string DropDatabaseSql = "DROP DATABASE keyload_tsi_ddl_fixture WITH (FORCE)";
    private const string DropRoleSql = "DROP ROLE keyload_tsi_role_fixture";
    private enum CleanupStage { Database, Role }
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);
    private readonly NpgsqlDataSource admin;
    private bool ownsDatabase;
    private bool ownsRole;

    private TimescaleTimeSeriesIntensiveSchemaFailureFixture(string connectionString)
    {
        RunId = Guid.NewGuid().ToString(GuidFormat);
        var settings = Settings(connectionString);
        settings.Database = AdminDatabase;
        admin = NpgsqlDataSource.Create(settings.ConnectionString);
        settings.Database = DatabaseName;
        PrivilegedConnectionString = settings.ConnectionString;
        settings.Options = "-c role=" + RoleName;
        RestrictedConnectionString = settings.ConnectionString;
    }

    internal string RunId { get; }
    internal string PrivilegedConnectionString { get; }
    internal string RestrictedConnectionString { get; }
    internal static string RestrictedRole => RoleName;
    internal ExceptionDispatchInfo? Primary { get; set; }

    internal static async Task<TimescaleTimeSeriesIntensiveSchemaFailureFixture> CreateAsync(
        string connectionString, CancellationToken token)
    {
        var fixture = new TimescaleTimeSeriesIntensiveSchemaFailureFixture(connectionString);
        try
        {
            await using var connection = await fixture.admin.OpenConnectionAsync(token).ConfigureAwait(false);
            await fixture.CreateRoleAsync(connection, token).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(CreateDatabaseSql, connection);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            fixture.ownsDatabase = true;
            return fixture;
        }
        catch (Exception error)
        {
            var original = ExceptionDispatchInfo.Capture(error);
            try
            {
                await fixture.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                original.Throw();
            }
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        using var cleanup = new CancellationTokenSource(CleanupTimeout, TimeProvider.System);
        ExceptionDispatchInfo? failure = null;
        if (ownsDatabase)
        {
            failure = await DropAsync(CleanupStage.Database, cleanup.Token)
                .ConfigureAwait(false);
        }
        if (ownsRole)
        {
            var roleFailure = await DropAsync(CleanupStage.Role, cleanup.Token).ConfigureAwait(false);
            failure ??= roleFailure;
        }
        try
        {
            await admin.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            failure ??= ExceptionDispatchInfo.Capture(error);
        }
        if (failure is not null)
        {
            Primary?.Throw();
            failure.Throw();
        }
    }

    private async Task CreateRoleAsync(NpgsqlConnection connection, CancellationToken token)
    {
        await using var command = new NpgsqlCommand(CreateRoleSql, connection);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        ownsRole = true;
    }

    private async Task<ExceptionDispatchInfo?> DropAsync(CleanupStage stage, CancellationToken token)
    {
        try
        {
            await using var connection = await admin.OpenConnectionAsync(token).ConfigureAwait(false);
            var sql = stage switch
            {
                CleanupStage.Database => DropDatabaseSql,
                CleanupStage.Role => DropRoleSql,
                _ => throw new ArgumentOutOfRangeException(nameof(stage))
            };
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (TimeSeriesIntensiveExceptionBoundary.IsNonfatal(error))
        {
            return ExceptionDispatchInfo.Capture(error);
        }
    }

    private static NpgsqlConnectionStringBuilder Settings(string connectionString) => new(connectionString)
    {
        Pooling = false,
        Enlist = false,
        IncludeErrorDetail = false,
        CommandTimeout = 30,
        CancellationTimeout = 2_000
    };
}
