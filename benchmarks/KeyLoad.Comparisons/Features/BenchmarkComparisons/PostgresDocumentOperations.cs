using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresDocumentOperations
{
    private const string ReadSql = "SELECT body::text FROM documents WHERE id=$1";
    private const string WriteSql = "INSERT INTO documents(id,body) VALUES ($1,$2)";
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

    internal static async Task WriteAsync(NpgsqlConnection connection, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = WriteSql;
        command.Parameters.AddWithValue(document.Id);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task SeedAsync(NpgsqlConnection connection, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        foreach (var document in dataset.Documents)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = SeedSql;
            command.Parameters.AddWithValue(document.Id);
            command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
            command.Parameters.AddWithValue(PostgresVectorOperations.VectorLiteral(document.Vector));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
