using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class PostgresSchemaOwnership
{
    private const string SetSchemaContextSql = "SELECT pg_catalog.set_config('keyload_test.schema_name', $1, true)";
    private const string SetForeignCommentSql = "DO $test$ BEGIN EXECUTE pg_catalog.format('COMMENT ON SCHEMA %I IS %L', current_setting('keyload_test.schema_name'), 'foreign-owner-marker'); END $test$";
    private const string ClearOwnerCommentSql = "DO $test$ BEGIN EXECUTE pg_catalog.format('COMMENT ON SCHEMA %I IS NULL', current_setting('keyload_test.schema_name')); END $test$";
    private const string DropSchemaSql = "DO $test$ BEGIN EXECUTE pg_catalog.format('DROP SCHEMA IF EXISTS %I CASCADE', current_setting('keyload_test.schema_name')); END $test$";

    internal static Task VerifyDuplicateTargetsAsync(string connectionString, CancellationToken cancellationToken)
        => PostgresSchemaDuplicateTargets.VerifyAsync(connectionString, cancellationToken);

    internal static async Task VerifyConservativeCleanupAsync(string connectionString, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("D");
        var schema = PostgresSchemaSupport.Schema(runId);
        var target = new PostgresTarget(connectionString, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        var disposeStarted = false;
        try
        {
            await target.InitializeAsync(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(PostgresSchemaSupport.Options(2))), cancellationToken);
            await MarkForeignOwnerAsync(connectionString, schema, cancellationToken);
            disposeStarted = true;
            await PostgresSchemaSupport.DisposeTargetAsync(target);
            await Assert.That(await PostgresSchemaSupport.SchemaExistsAsync(connectionString, schema, cancellationToken)).IsTrue();
        }
        finally
        {
            try
            {
                if (!disposeStarted)
                {
                    await PostgresSchemaSupport.DisposeTargetAsync(target);
                }
            }
            finally
            {
                await DropOwnedSchemaAsync(connectionString, schema, CancellationToken.None);
            }
        }
        await VerifyMissingSchemaCleanupAsync(connectionString, cancellationToken);
        await VerifyMissingOwnerCommentCleanupAsync(connectionString, cancellationToken);
    }

    private static async Task VerifyMissingOwnerCommentCleanupAsync(string connectionString, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("D");
        var schema = PostgresSchemaSupport.Schema(runId);
        var target = new PostgresTarget(connectionString, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        var disposeStarted = false;
        try
        {
            await target.InitializeAsync(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(PostgresSchemaSupport.Options(2))), cancellationToken);
            await ClearOwnerCommentAsync(connectionString, schema, cancellationToken);
            disposeStarted = true;
            await PostgresSchemaSupport.DisposeTargetAsync(target);
            await Assert.That(await PostgresSchemaSupport.SchemaExistsAsync(connectionString, schema, cancellationToken)).IsTrue();
        }
        finally
        {
            try
            {
                if (!disposeStarted)
                {
                    await PostgresSchemaSupport.DisposeTargetAsync(target);
                }
            }
            finally
            {
                await DropOwnedSchemaAsync(connectionString, schema, CancellationToken.None);
            }
        }
    }

    private static async Task VerifyMissingSchemaCleanupAsync(string connectionString, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("D");
        var schema = PostgresSchemaSupport.Schema(runId);
        var target = new PostgresTarget(connectionString, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        var disposeStarted = false;
        try
        {
            await target.InitializeAsync(new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(PostgresSchemaSupport.Options(2))), cancellationToken);
            await DropOwnedSchemaAsync(connectionString, schema, cancellationToken);
            disposeStarted = true;
            await PostgresSchemaSupport.DisposeTargetAsync(target);
            await Assert.That(await PostgresSchemaSupport.SchemaExistsAsync(connectionString, schema, cancellationToken)).IsFalse();
        }
        finally
        {
            try
            {
                if (!disposeStarted)
                {
                    await PostgresSchemaSupport.DisposeTargetAsync(target);
                }
            }
            finally
            {
                await DropOwnedSchemaAsync(connectionString, schema, CancellationToken.None);
            }
        }
    }

    private static async Task MarkForeignOwnerAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using var connection = await PostgresSchemaSupport.OpenAsync(connectionString, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetSchemaContextAsync(connection, transaction, schema, cancellationToken);
        await using var command = new NpgsqlCommand(SetForeignCommentSql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ClearOwnerCommentAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using var connection = await PostgresSchemaSupport.OpenAsync(connectionString, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetSchemaContextAsync(connection, transaction, schema, cancellationToken);
        await using var command = new NpgsqlCommand(ClearOwnerCommentSql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task DropOwnedSchemaAsync(string connectionString, string schema,
        CancellationToken cancellationToken = default)
    {
        var policy = NativeExecutionPolicyFixture.Harness().Value;
        using var cleanup = cancellationToken.CanBeCanceled ? null : new CancellationTokenSource(policy.PostgresSchemaCleanupTimeout);
        var token = cancellationToken.CanBeCanceled ? cancellationToken : cleanup!.Token;
        await using var connection = await PostgresSchemaSupport.OpenAsync(connectionString, token);
        await using var transaction = await connection.BeginTransactionAsync(token);
        await SetSchemaContextAsync(connection, transaction, schema, token);
        await using var command = new NpgsqlCommand(DropSchemaSql, connection, transaction);
        await command.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
    }

    private static async Task SetSchemaContextAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string schema, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SetSchemaContextSql, connection, transaction);
        command.Parameters.AddWithValue(schema);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
