using System.Globalization;
using System.Text;
using KeyLoad.ComparisonHost.Deployment;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.ComparisonHost.Vectors;

/// <summary>
/// PostgreSQL with pgvector through Npgsql: a <c>vector(n)</c> column with no ANN index, so
/// <c>ORDER BY embedding &lt;=&gt; $1 LIMIT k</c> is an exact cosine scan; vectors travel in pgvector's text format and a
/// batch is one multi-row <c>INSERT ... unnest</c>. With standbys the primary commits only after a synchronous
/// <c>ANY 1</c> quorum of the two standbys flushed the WAL.
/// </summary>
internal sealed class PgVectorDriver(BenchmarkConnection connection) : IVectorDriver
{
    private const string CreateExtensionSql = "CREATE EXTENSION IF NOT EXISTS vector";
    private const string SetDimensionsSql = "SELECT set_config('benchmark.dimensions', $1::text, false)";
    private const string CreateTableSql = """
        DO $table$
        BEGIN
            EXECUTE format('CREATE TABLE vectors (id text COLLATE "C" PRIMARY KEY, embedding vector(%s) NOT NULL)',
                current_setting('benchmark.dimensions')::integer);
        END
        $table$
        """;
    private const string InsertSql = "INSERT INTO vectors (id, embedding) SELECT u.id, u.embedding::vector FROM unnest($1::text[], $2::text[]) AS u(id, embedding)";
    private const string NearestSql = "SELECT id FROM vectors ORDER BY embedding <=> $1::vector, id LIMIT $2";
    private const string FloatFormat = "R";
    private const char VectorOpen = '[';
    private const char VectorClose = ']';
    private const char ComponentSeparator = ',';
    private const int CharactersPerComponent = 14;
    private const int MaxAutoPrepare = 32;
    private const int AutoPrepareMinUsages = 1;
    private const string WrongInsertCount = "PostgreSQL inserted a different number of rows: ";
    private readonly NpgsqlDataSource source = Open(connection.RequireConnectionString());

    public int BatchSize => 100;

    /// <summary>On a cluster, first makes commits wait for the standby quorum; then creates the extension and the table.</summary>
    public async Task PrepareAsync(int dimensions, CancellationToken token)
    {
        await PostgresQuorum.RequireAsync(source, connection.Nodes, token);
        await using var session = await source.OpenConnectionAsync(token);
        await using (var extension = new NpgsqlCommand(CreateExtensionSql, session))
        {
            _ = await extension.ExecuteNonQueryAsync(token);
        }
        await using (var setting = new NpgsqlCommand(SetDimensionsSql, session))
        {
            setting.Parameters.AddWithValue(NpgsqlDbType.Integer, dimensions);
            _ = await setting.ExecuteScalarAsync(token);
        }
        await using var table = new NpgsqlCommand(CreateTableSql, session);
        _ = await table.ExecuteNonQueryAsync(token);
    }

    public async Task InsertAsync(IReadOnlyList<VectorRecord> vectors, CancellationToken token)
    {
        var ids = new string[vectors.Count];
        var embeddings = new string[vectors.Count];
        for (var index = 0; index < vectors.Count; index++)
        {
            ids[index] = vectors[index].Id;
            embeddings[index] = Literal(vectors[index].Values);
        }
        await using var command = source.CreateCommand(InsertSql);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, ids);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, embeddings);
        var inserted = await command.ExecuteNonQueryAsync(token);
        if (inserted != vectors.Count)
        {
            throw new InvalidOperationException(WrongInsertCount + inserted.ToString(CultureInfo.InvariantCulture));
        }
    }

    public async Task<IReadOnlyList<string>> NearestAsync(float[] query, int limit, CancellationToken token)
    {
        await using var command = source.CreateCommand(NearestSql);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, Literal(query));
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, limit);
        await using var reader = await command.ExecuteReaderAsync(token);
        var ids = new List<string>(limit);
        while (await reader.ReadAsync(token))
        {
            ids.Add(reader.GetString(0));
        }
        return ids;
    }

    public ValueTask DisposeAsync() => source.DisposeAsync();

    private static NpgsqlDataSource Open(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.ConnectionStringBuilder.MaxAutoPrepare = MaxAutoPrepare;
        builder.ConnectionStringBuilder.AutoPrepareMinUsages = AutoPrepareMinUsages;
        return builder.Build();
    }

    /// <summary>pgvector's text input format: <c>[x1,x2,...]</c> with round-trip float32 components.</summary>
    private static string Literal(float[] values)
    {
        var text = new StringBuilder((values.Length * CharactersPerComponent) + 2);
        text.Append(VectorOpen);
        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                text.Append(ComponentSeparator);
            }
            text.Append(values[index].ToString(FloatFormat, CultureInfo.InvariantCulture));
        }
        return text.Append(VectorClose).ToString();
    }
}
