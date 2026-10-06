using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresStreamOperations
{
    private const string InsertEventSql = "INSERT INTO events(stream_id,event_id,revision,body) VALUES ($1,$2,1,$3)";
    private const string ReadEventSql = "SELECT event_id,revision,body::text FROM events WHERE stream_id=$1 LIMIT 2";
    private const string CardinalityFailure = "PostgresStreamCardinality";

    internal static async Task SeedAsync(NpgsqlConnection connection, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        foreach (var document in dataset.Documents)
        {
            await using var command = EventInsert(connection, document);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    internal static async Task AppendAsync(NpgsqlConnection connection, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        await using var command = EventInsert(connection, document);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<FoundEvent?> ReadAsync(NpgsqlConnection connection, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;
        const int ThirdColumnIndex = 2;

        await using var command = connection.CreateCommand();
        command.CommandText = ReadEventSql;
        command.Parameters.AddWithValue(document.Id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var found = new FoundEvent(reader.GetGuid(FirstColumnIndex), checked((ulong)reader.GetInt64(SecondColumnIndex)), reader.GetString(ThirdColumnIndex));
        if (await reader.ReadAsync(cancellationToken))
        {
            throw new ComparisonFailureException(CardinalityFailure);
        }

        return found;
    }

    private static NpgsqlCommand EventInsert(NpgsqlConnection connection, BenchmarkDocument document)
    {
        var command = connection.CreateCommand();
        command.CommandText = InsertEventSql;
        command.Parameters.AddWithValue(document.Id);
        command.Parameters.AddWithValue(BenchmarkDataset.EventId(document));
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, document.Json);
        return command;
    }
}
