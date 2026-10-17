using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresSchemaLifecycle
{
    private const int MinimumDimensions = 2;
    private const int MaximumDimensions = 1024;

    internal static async Task CreateAsync(NpgsqlConnection connection, PostgresSchemaIdentity identity,
        Guid ownerGuid, int dimensions, Action markCommitAttempted, CancellationToken cancellationToken)
    {
        ValidateDimensions(dimensions);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetCreateContextAsync(connection, transaction, identity, ownerGuid, dimensions, cancellationToken);
        await AcquireNamespaceLockAsync(connection, transaction, identity.LockKey, cancellationToken);
        await using (var extension = new NpgsqlCommand(PostgresSchemaCommands.CreateExtension, connection, transaction))
        {
            await extension.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var create = new NpgsqlCommand(PostgresSchemaCommands.CreateOwnedSchema, connection, transaction))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
        // This only triggers marker-checked cleanup; it never authorizes schema deletion.
        markCommitAttempted();
        await transaction.CommitAsync(cancellationToken);
    }

    internal static async Task DropIfOwnedAsync(NpgsqlConnection connection, PostgresSchemaIdentity identity,
        Guid ownerGuid, CancellationToken cancellationToken = default)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetCleanupContextAsync(connection, transaction, identity, ownerGuid, cancellationToken);
        await AcquireNamespaceLockAsync(connection, transaction, identity.LockKey, cancellationToken);
        await using (var drop = new NpgsqlCommand(PostgresSchemaCommands.DropIfOwnedSchema, connection, transaction))
        {
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ValidateDimensions(int dimensions)
    {
        if (dimensions is < MinimumDimensions or > MaximumDimensions)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions,
                PostgresSchemaCommands.DimensionRangeError);
        }
    }

    private static async Task SetCreateContextAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        PostgresSchemaIdentity identity, Guid ownerGuid, int dimensions, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PostgresSchemaCommands.SetCreateContext, connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, identity.RunGuid);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, ownerGuid);
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, dimensions);
        command.Parameters.AddWithValue(NpgsqlDbType.Bigint, identity.LockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetCleanupContextAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        PostgresSchemaIdentity identity, Guid ownerGuid, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PostgresSchemaCommands.SetCleanupContext, connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, identity.RunGuid);
        command.Parameters.AddWithValue(NpgsqlDbType.Uuid, ownerGuid);
        command.Parameters.AddWithValue(NpgsqlDbType.Bigint, identity.LockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AcquireNamespaceLockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        long lockKey, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PostgresSchemaCommands.AcquireNamespaceLock, connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Bigint, lockKey);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
