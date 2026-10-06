using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using Npgsql;

namespace KeyLoad.Comparisons.Targets;

internal static class PostgresVectorOperations
{
    private const string VectorLiteralOpenBracket = "[";
    private const string ValueSeparator = ",";
    private const string VectorLiteralCloseBracket = "]";

    private const string VectorValueFormat = "R";

    private const string SearchSql = "SELECT id,body::text FROM documents WHERE embedding IS NOT NULL ORDER BY embedding <=> $1::vector, id LIMIT $2";

    internal static string VectorLiteral(ImmutableArray<float> vector)
        => VectorLiteralOpenBracket + string.Join(ValueSeparator, vector.Select(value => value.ToString(VectorValueFormat, CultureInfo.InvariantCulture))) + VectorLiteralCloseBracket;

    internal static async Task<ImmutableArray<FoundDocument>> SearchAsync(NpgsqlConnection connection,
        BenchmarkDocument document, int topK, CancellationToken cancellationToken)
    {
        const int FirstColumnIndex = 0;
        const int SecondColumnIndex = 1;

        await using var command = connection.CreateCommand();
        command.CommandText = SearchSql;
        command.Parameters.AddWithValue(VectorLiteral(document.Vector));
        command.Parameters.AddWithValue(topK);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var found = new List<FoundDocument>();
        while (await reader.ReadAsync(cancellationToken))
        {
            found.Add(new(reader.GetString(FirstColumnIndex), reader.GetString(SecondColumnIndex)));
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(found.ToArray());
    }
}
