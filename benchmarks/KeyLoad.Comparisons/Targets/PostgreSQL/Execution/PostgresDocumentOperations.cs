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
    private const string CopySql = "COPY documents(id,body) FROM STDIN (FORMAT BINARY)";

    internal static async Task<(bool HasValue, FoundDocument? Value)> ReadAsync(NpgsqlConnection connection, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = ReadSql;
        command.Parameters.AddWithValue(document.Id);
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json)
        {
            return (false, default(FoundDocument?));
        }

        return (true, (FoundDocument?)(new(document.Id, json)));
    }

    internal static Task WriteAsync(NpgsqlConnection connection, BenchmarkDocument document, CancellationToken cancellationToken)
        => ExecuteMutationAsync(connection, Scenario.DocumentWrite, document, cancellationToken);

    // One COPY statement commits every row in one implicit transaction under the same synchronous_commit as a single INSERT.
    internal static async Task CreateAsync(NpgsqlConnection connection, IReadOnlyList<BenchmarkDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        try
        {
            if (await CopyAsync(connection, documents, cancellationToken).ConfigureAwait(false) != checked((ulong)documents.Count))
            {
                throw new ComparisonFailureException(ComparisonMutationFailures.CardinalityMismatch);
            }
        }
        catch (PostgresException error) when (error.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ComparisonFailureException(ComparisonMutationFailures.CreateConflict, error);
        }
    }

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
            await CreateAsync(connection, dataset.Documents, cancellationToken);
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

    private static async Task<ulong> CopyAsync(NpgsqlConnection connection, IReadOnlyList<BenchmarkDocument> documents,
        CancellationToken cancellationToken)
    {
        await using var copy = await connection.BeginBinaryImportAsync(CopySql, cancellationToken).ConfigureAwait(false);
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await copy.StartRowAsync(cancellationToken).ConfigureAwait(false);
            await copy.WriteAsync(document.Id, NpgsqlDbType.Text, cancellationToken).ConfigureAwait(false);
            await copy.WriteAsync(document.Json, NpgsqlDbType.Jsonb, cancellationToken).ConfigureAwait(false);
        }
        return await copy.CompleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
