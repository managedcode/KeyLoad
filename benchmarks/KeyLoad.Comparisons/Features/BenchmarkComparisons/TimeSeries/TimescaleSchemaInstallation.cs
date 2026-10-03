using System.Runtime.ExceptionServices;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class TimescaleSchemaInstallation
{
    private const string OwnerMarkerMismatch = "TimescaleOwnerMarkerMismatch";
    private enum Statement { CreateSchema, CreateExtension, CreateMarker, LockMarker, DropSchema }

    internal static async Task<bool> InitializeAsync(NpgsqlConnection connection, string schemaName, Guid ownerId,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> installer, CancellationToken token)
    {
        await connection.OpenAsync(token).ConfigureAwait(false);
        await TimescaleSchemaLifecycle.ConfigureSchemaAsync(connection, schemaName, token).ConfigureAwait(false);
        await InstallTransactionAsync(connection, schemaName, ownerId, installer, token).ConfigureAwait(false);
        return await TimescaleSchemaLifecycle.MarkerMatchesAsync(connection, ownerId, token).ConfigureAwait(false);
    }

    internal static async Task DropAsync(NpgsqlConnection connection, string schemaName, Guid ownerId,
        CancellationToken token)
    {
        await connection.OpenAsync(token).ConfigureAwait(false);
        await TimescaleSchemaLifecycle.ConfigureSchemaAsync(connection, schemaName, token).ConfigureAwait(false);
        await TimescaleSchemaLifecycle.SetSearchPathAsync(connection, schemaName, token).ConfigureAwait(false);
        await DropTransactionAsync(connection, ownerId, token).ConfigureAwait(false);
    }

    private static async Task InstallTransactionAsync(NpgsqlConnection connection, string schemaName, Guid ownerId,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task> installer, CancellationToken token)
    {
        var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        ExceptionDispatchInfo? primary = null;
        try
        {
            await ExecuteAsync(Statement.CreateSchema, connection, transaction, token).ConfigureAwait(false);
            await TimescaleSchemaLifecycle.SetSearchPathAsync(connection, schemaName, token, transaction).ConfigureAwait(false);
            await ExecuteAsync(Statement.CreateExtension, connection, transaction, token).ConfigureAwait(false);
            await ExecuteAsync(Statement.CreateMarker, connection, transaction, token).ConfigureAwait(false);
            await using (var marker = new NpgsqlCommand(TimescaleSchemaLifecycle.InsertMarkerSql, connection, transaction))
            {
                marker.Parameters.AddWithValue(NpgsqlDbType.Uuid, ownerId);
                await marker.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            await installer(connection, transaction, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
            throw;
        }
        finally
        {
            await DisposeOriginalAsync(transaction, primary).ConfigureAwait(false);
        }
    }

    private static async Task DropTransactionAsync(NpgsqlConnection connection, Guid ownerId, CancellationToken token)
    {
        var transaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        ExceptionDispatchInfo? primary = null;
        try
        {
            await ExecuteAsync(Statement.LockMarker, connection, transaction, token).ConfigureAwait(false);
            if (!await TimescaleSchemaLifecycle.MarkerMatchesAsync(connection, ownerId, token, transaction).ConfigureAwait(false))
            {
                throw TimeSeriesComparisonTargetErrors.Create(OwnerMarkerMismatch);
            }
            await ExecuteAsync(Statement.DropSchema, connection, transaction, token).ConfigureAwait(false);
            await transaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
            throw;
        }
        finally
        {
            await DisposeOriginalAsync(transaction, primary).ConfigureAwait(false);
        }
    }

    private static async Task ExecuteAsync(Statement statement, NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken token)
    {
        var sql = statement switch
        {
            Statement.CreateSchema => TimescaleSchemaLifecycle.CreateSchemaSql,
            Statement.CreateExtension => TimescaleSchemaLifecycle.CreateExtensionSql,
            Statement.CreateMarker => TimescaleSchemaLifecycle.CreateMarkerSql,
            Statement.LockMarker => TimescaleSchemaLifecycle.LockMarkerSql,
            Statement.DropSchema => TimescaleSchemaLifecycle.DropSchemaSql,
            _ => throw new ArgumentOutOfRangeException(nameof(statement))
        };
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async ValueTask DisposeOriginalAsync(NpgsqlTransaction disposable, ExceptionDispatchInfo? primary)
    {
        try
        {
            await disposable.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            primary?.Throw();
            throw;
        }
    }
}
