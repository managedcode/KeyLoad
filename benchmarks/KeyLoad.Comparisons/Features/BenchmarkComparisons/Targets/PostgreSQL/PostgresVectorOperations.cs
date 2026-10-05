using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresVectorOperations
{
    private const string VectorValueFormat = "R";

    private const string SearchSql = "SELECT id,body::text FROM documents WHERE embedding IS NOT NULL ORDER BY embedding <=> $1::vector, id LIMIT $2";

    internal static string VectorLiteral(ImmutableArray<float> vector)
        => "[" + string.Join(",", vector.Select(value => value.ToString(VectorValueFormat, CultureInfo.InvariantCulture))) + "]";

    internal static async Task<ImmutableArray<FoundDocument>> SearchAsync(NpgsqlConnection connection,
        BenchmarkDocument document, int topK, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SearchSql;
        command.Parameters.AddWithValue(VectorLiteral(document.Vector));
        command.Parameters.AddWithValue(topK);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var found = new List<FoundDocument>();
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new(reader.GetString(0), reader.GetString(1)));
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(found.ToArray());
    }
}
