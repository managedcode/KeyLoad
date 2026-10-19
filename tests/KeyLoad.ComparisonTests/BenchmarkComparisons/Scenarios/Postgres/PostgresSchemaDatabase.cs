using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

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
        var policy = ExecutionPolicyFixture.Harness().Value;
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
