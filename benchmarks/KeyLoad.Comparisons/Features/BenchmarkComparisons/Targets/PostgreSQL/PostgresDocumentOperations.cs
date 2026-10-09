using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresDocumentOperations
{
    private const string ReadSql = "SELECT body::text FROM documents WHERE id=$1";
    private const string WriteSql = "INSERT INTO documents(id,body) VALUES ($1,$2)";
    private const string UpdateSql = "UPDATE documents SET body=$2 WHERE id=$1";
    private const string DeleteSql = "DELETE FROM documents WHERE id=$1";
    private const string SeedSql = "INSERT INTO documents(id,body,embedding) VALUES ($1,$2,$3::vector)";

    internal static async Task<FoundDocument?> ReadAsync(NpgsqlConnection connection, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ReadSql;
        command.Parameters.AddWithValue(document.Id);
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json)
        {
            return null;
        }

        return new(document.Id, json);
    }

    internal static Task WriteAsync(NpgsqlConnection connection, BenchmarkDocument document, CancellationToken cancellationToken)
        => ExecuteMutationAsync(connection, Scenario.DocumentWrite, document, cancellationToken);

    internal static Task UpdateAsync(NpgsqlConnection connection, BenchmarkDocument document, CancellationToken cancellationToken)
        => ExecuteMutationAsync(connection, Scenario.DocumentUpdate, document, cancellationToken);

    internal static Task DeleteAsync(NpgsqlConnection connection, BenchmarkDocument document, CancellationToken cancellationToken)
        => ExecuteMutationAsync(connection, Scenario.DocumentDelete, document, cancellationToken);

    internal static NpgsqlCommand CreateCommand(NpgsqlConnection connection, Scenario scenario, BenchmarkDocument document)
    {
        var sql = scenario switch
        {
            Scenario.DocumentWrite => WriteSql,
            Scenario.DocumentUpdate => UpdateSql,
            Scenario.DocumentDelete => DeleteSql,
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, document.Id);
        if (scenario != Scenario.DocumentDelete)
        {
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
        }
        return command;
    }

    internal static void RequireAffected(Scenario scenario, int affected)
    {
        const int SingleItemCount = 1;
        const int NoObservedItems = 0;

        if (affected == SingleItemCount)
        {
            return;
        }
        var code = affected == NoObservedItems ? scenario switch
        {
            Scenario.DocumentUpdate => ComparisonMutationFailures.UpdateMissing,
            Scenario.DocumentDelete => ComparisonMutationFailures.DeleteMissing,
            _ => ComparisonMutationFailures.CardinalityMismatch,
        } : ComparisonMutationFailures.CardinalityMismatch;
        throw new ComparisonFailureException(code);
    }

    private static async Task ExecuteMutationAsync(NpgsqlConnection connection, Scenario scenario,
        BenchmarkDocument document, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, scenario, document);
        try
        {
            RequireAffected(scenario, await command.ExecuteNonQueryAsync(cancellationToken));
        }
        catch (PostgresException error) when (scenario == Scenario.DocumentWrite && error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict, error);
        }
    }

    internal static async Task SeedAsync(NpgsqlConnection connection, IComparisonCorpus dataset,
        CancellationToken cancellationToken)
    {
        if ((dataset.Settings is ScaledComparisonProfile || dataset is DocumentComparisonCorpus))
        {
            await SeedScaledAsync(connection, dataset.Documents, cancellationToken);
            return;
        }
        foreach (var document in dataset.Documents)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = SeedSql;
            command.Parameters.AddWithValue(document.Id);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
            command.Parameters.AddWithValue(PostgresVectorOperations.VectorLiteral(document.Vector));
            RequireAffected(Scenario.DocumentWrite, await command.ExecuteNonQueryAsync(cancellationToken));
        }
    }

    private static async Task SeedScaledAsync(NpgsqlConnection connection, IReadOnlyList<BenchmarkDocument> documents,
        CancellationToken cancellationToken)
    {
        const string COPYDocumentsIdBodyFROMSTDINFORMATBINARYToken = "COPY documents(id,body) FROM STDIN (FORMAT BINARY)";

        await using var copy = await connection.BeginBinaryImportAsync(
            COPYDocumentsIdBodyFROMSTDINFORMATBINARYToken, cancellationToken).ConfigureAwait(false);
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await copy.StartRowAsync(cancellationToken).ConfigureAwait(false);
            await copy.WriteAsync(document.Id, NpgsqlDbType.Text, cancellationToken).ConfigureAwait(false);
            await copy.WriteAsync(document.Json, NpgsqlDbType.Jsonb, cancellationToken).ConfigureAwait(false);
        }
        await copy.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
