using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class PostgresSchemaFailure
{
    private const string WaitingLockSql = "SELECT EXISTS(SELECT 1 FROM pg_catalog.pg_locks l JOIN pg_catalog.pg_stat_activity a USING(pid) WHERE l.locktype='advisory' AND NOT l.granted AND l.classid::bigint=(($1::bigint >> 32) & 4294967295) AND l.objid::bigint=($1::bigint & 4294967295) AND l.objsubid=1 AND a.wait_event_type='Lock')";
    private const string AcquireNamespaceLockSql = "SELECT pg_catalog.pg_advisory_xact_lock($1)";
    private const string ExtensionFailureSchema = "keyload_unrelated_schema";

    internal static async Task VerifyAtomicFailureAsync(string connectionString, CancellationToken cancellationToken)
    {
        var policy = NativeExecutionPolicyFixture.Harness().Value;
        var isolated = await PostgresSchemaDatabase.CreateAsync(connectionString, cancellationToken);
        var runId = Guid.NewGuid().ToString("D");
        var schema = PostgresSchemaSupport.Schema(runId);
        var target = new PostgresTarget(isolated, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        try
        {
            await PostgresSchemaDatabase.InstallVectorOutsideSearchPathAsync(isolated, cancellationToken);
            await PostgresSchemaDatabase.CreateUnrelatedSchemaAsync(isolated, cancellationToken);
            var failure = await CapturePostgresFailureAsync(target, cancellationToken);
            await Assert.That(failure).IsTypeOf<PostgresException>();
            await Assert.That(((PostgresException)failure!).SqlState).IsEqualTo(PostgresErrorCodes.UndefinedObject);
            await Assert.That(await PostgresSchemaSupport.SchemaExistsAsync(isolated, schema, cancellationToken)).IsFalse();
            await Assert.That(await PostgresSchemaSupport.SchemaExistsAsync(isolated, ExtensionFailureSchema, cancellationToken)).IsTrue();
        }
        finally
        {
            try
            {
                await PostgresSchemaOriginalTaskSettlement.JoinAsync(target.DisposeAsync().AsTask(),
                    policy.PostgresTargetDisposeTimeout, CancellationToken.None);
            }
            finally
            {
                await PostgresSchemaDatabase.DropAsync(connectionString);
            }
        }
        await VerifySubsequentRunAsync(connectionString, Guid.NewGuid().ToString("D"), cancellationToken);
    }

    internal static async Task VerifyLockCancellationAsync(string connectionString, CancellationToken cancellationToken)
    {
        var harnessOptions = NativeExecutionPolicyFixture.Harness();
        var runId = Guid.NewGuid().ToString("D");
        var schema = PostgresSchemaSupport.Schema(runId);
        var lockKey = PostgresSchemaSupport.LockKey(Guid.Parse(runId));
        await using var blocker = await PostgresSchemaSupport.OpenAsync(connectionString, cancellationToken);
        await using var transaction = await blocker.BeginTransactionAsync(cancellationToken);
        await AcquireLockAsync(blocker, transaction, lockKey, cancellationToken);
        var failures = new IsolatedNativeTeardownFailures(null);
        await IsolatedNativeOriginalTaskSettlement.RunAsync(() => PostgresSchemaLockOwner.RunAsync(
            connectionString, runId, blocker, transaction, lockKey, harnessOptions, failures, cancellationToken),
            "postgres-owned-target", failures, harnessOptions,
            () => PostgresSchemaLockOwner.ReleaseBlockerAsync(blocker, transaction, failures, harnessOptions));
        failures.ThrowIfAny();
        await Assert.That(await PostgresSchemaSupport.SchemaExistsAsync(connectionString, schema, cancellationToken)).IsFalse();
        await VerifySubsequentRunAsync(connectionString, runId, cancellationToken);
    }

    internal static async Task WaitForAdvisoryLockWaitAsync(NpgsqlConnection connection, long lockKey,
        CancellationToken cancellationToken)
    {
        var policy = NativeExecutionPolicyFixture.Harness().Value;
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        while (clock.GetElapsedTime(started) < policy.PostgresLockObservationTimeout)
        {
            await using var command = new NpgsqlCommand(WaitingLockSql, connection);
            command.Parameters.AddWithValue(lockKey);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
            {
                return;
            }
            await Task.Delay(policy.PostgresLockPollInterval, clock, cancellationToken);
        }
        throw new TimeoutException("PostgreSQL did not report the target waiting for its namespace advisory lock.");
    }

    private static async Task AcquireLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long lockKey, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(AcquireNamespaceLockSql, connection, transaction);
        command.Parameters.AddWithValue(lockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<PostgresException?> CapturePostgresFailureAsync(PostgresTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            await target.InitializeAsync(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(PostgresSchemaSupport.Options(2))), cancellationToken);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception;
        }
    }

    internal static async Task<OperationCanceledException?> CaptureCancellationAsync(PostgresTarget target,
        CancellationToken cancellationToken)
    {
        try
        {
            await target.InitializeAsync(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(PostgresSchemaSupport.Options(2))), cancellationToken);
            return null;
        }
        catch (OperationCanceledException exception)
        {
            return exception;
        }
    }

    private static async Task VerifySubsequentRunAsync(string connectionString, string runId,
        CancellationToken cancellationToken)
    {
        var target = new PostgresTarget(connectionString, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        try
        {
            await target.InitializeAsync(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(PostgresSchemaSupport.Options(2))), cancellationToken);
        }
        finally
        {
            await PostgresSchemaSupport.DisposeTargetAsync(target);
        }
        await Assert.That(await PostgresSchemaSupport.SchemaExistsAsync(connectionString,
            PostgresSchemaSupport.Schema(runId), cancellationToken)).IsFalse();
    }
}

internal static class PostgresSchemaDatabase
{
    internal const string DatabaseName = "keyload_schema_atomic_failure";
    private const string AdminDatabaseName = "postgres";
    private const string CreateDatabaseSql = "CREATE DATABASE keyload_schema_atomic_failure";
    private const string DropDatabaseSql = "DROP DATABASE IF EXISTS keyload_schema_atomic_failure WITH (FORCE)";
    private const string CreateTestSchemaSql = "CREATE SCHEMA keyload_unrelated_schema";
    private const string CreateExtensionSchemaSql = "CREATE SCHEMA keyload_vector_private; CREATE EXTENSION vector WITH SCHEMA keyload_vector_private";

    internal static async Task<string> CreateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = AdminDatabaseName }.ConnectionString;
        await using var connection = await PostgresSchemaSupport.OpenAsync(admin, cancellationToken);
        await using var command = new NpgsqlCommand(CreateDatabaseSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new NpgsqlConnectionStringBuilder(connectionString) { Database = DatabaseName }.ConnectionString;
    }

    internal static async Task InstallVectorOutsideSearchPathAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = await PostgresSchemaSupport.OpenAsync(connectionString, cancellationToken);
        await using var command = new NpgsqlCommand(CreateExtensionSchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task CreateUnrelatedSchemaAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = await PostgresSchemaSupport.OpenAsync(connectionString, cancellationToken);
        await using var command = new NpgsqlCommand(CreateTestSchemaSql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task DropAsync(string connectionString)
    {
        var policy = NativeExecutionPolicyFixture.Harness().Value;
        using var cleanup = new CancellationTokenSource(policy.PostgresDatabaseCleanupTimeout, TimeProvider.System);
        var ownedPool = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Database = DatabaseName }.ConnectionString);
        NpgsqlConnection.ClearPool(ownedPool);
        await ownedPool.DisposeAsync();
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = AdminDatabaseName }.ConnectionString;
        await using var connection = await PostgresSchemaSupport.OpenAsync(admin, cleanup.Token);
        await using var command = new NpgsqlCommand(DropDatabaseSql, connection);
        await command.ExecuteNonQueryAsync(cleanup.Token);
    }
}
