using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Executes exact, HNSW and IVFFlat profiles with pgvector's native PostgreSQL operators.</summary>
public sealed class PostgresNativeVectorTarget(string connectionString, string runId, string image)
    : IVectorComparisonTarget
{
    private readonly string schema = "kv_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(runId)))[..20];
    private readonly NpgsqlDataSource source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString)
    {
        MaxPoolSize = 24,
        CommandTimeout = 30,
        SearchPath = "public"
    }.ConnectionString);
    private bool initialized;
    private VectorComparisonProfile? profile;
    private int ivfProbes;

    private string Table => $"\"{schema}\".vectors";
    public string Name => "PostgreSQL + pgvector";
    public TargetProfile Profile { get; private set; } = new("PostgreSQL + pgvector", "PostgreSQL; pgvector 0.8.6 image", "standalone primary",
        "synchronous_commit=on; PostgreSQL WAL local commit", "READ COMMITTED native vector readback",
        "Npgsql pooled TCP; pgvector native operators", "isolated benchmark schema; database owner", image);

    public bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode)
        => indexKind is VectorIndexKind.Exact or VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat;

    public async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        var batch = new List<VectorDocument>(512);
        await foreach (var item in documents.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            batch.Add(item);
            if (batch.Count == batch.Capacity)
            {
                await InsertBatchAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
                count += batch.Count;
                batch.Clear();
            }
        }
        if (batch.Count > 0)
        {
            await InsertBatchAsync(connection, transaction, batch, cancellationToken).ConfigureAwait(false);
            count += batch.Count;
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    public async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile selected, CancellationToken cancellationToken)
    {
        profile = selected ?? throw new ArgumentNullException(nameof(selected));
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        if (selected.IndexKind == VectorIndexKind.Exact)
            return new(VectorIndexKind.Exact, "No ANN index; native exact cosine distance order", new Dictionary<string, string>(), 0);
        await using (var analyze = new NpgsqlCommand($"ANALYZE {Table}", connection))
            await analyze.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var timer = Stopwatch.StartNew();
        string sql;
        IReadOnlyDictionary<string, string> parameters;
        if (selected.IndexKind == VectorIndexKind.Hnsw)
        {
            sql = $"CREATE INDEX vectors_hnsw_idx ON {Table} USING hnsw (embedding vector_cosine_ops) WITH (m = 16, ef_construction = 200)";
            parameters = new Dictionary<string, string> { ["m"] = "16", ["efConstruction"] = "200", ["efSearch"] = "200" };
        }
        else if (selected.IndexKind == VectorIndexKind.IvfFlat)
        {
            var lists = Math.Max(1, (int)Math.Sqrt(selected.RecordCount));
            ivfProbes = Math.Min(lists, (int)Math.Ceiling(Math.Sqrt(lists)) * 4);
            sql = $"CREATE INDEX vectors_ivfflat_idx ON {Table} USING ivfflat (embedding vector_cosine_ops) WITH (lists = {lists})";
            parameters = new Dictionary<string, string>
            {
                ["lists"] = lists.ToString(CultureInfo.InvariantCulture),
                ["probes"] = ivfProbes.ToString(CultureInfo.InvariantCulture),
                ["iterativeScan"] = "relaxed_order"
            };
        }
        else throw new NotSupportedException("PostgreSQL does not expose the selected native vector index.");

        await using (var command = new NpgsqlCommand(sql, connection))
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        timer.Stop();
        return new(selected.IndexKind, sql, parameters, timer.Elapsed.TotalMilliseconds);
    }

    public async IAsyncEnumerable<VectorReadback> ReadbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"SELECT number, id, embedding::text, payload FROM {Table} ORDER BY number", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var number = reader.GetInt32(0);
            var id = reader.GetString(1);
            var vector = ParseVector(reader.GetString(2));
            var payload = reader.GetString(3);
            yield return new(number, id, vector.Length, VectorComparisonCorpus.HashVector(vector),
                Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
        }
    }

    public async Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken cancellationToken)
    {
        var selected = profile ?? throw new InvalidOperationException("Vector index must be built before queries.");
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetSearchSettingsAsync(connection, selected.IndexKind, cancellationToken).ConfigureAwait(false);
        if (selected.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat)
        {
            await using var force = new NpgsqlCommand("SET enable_seqscan = off", connection);
            await force.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var sql = $"SELECT id,distance FROM (SELECT id,embedding <=> $1::vector AS distance FROM {Table}{Filter(mode)} ORDER BY embedding <=> $1::vector LIMIT $2) AS candidates ORDER BY distance,id";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(VectorLiteral(query.Span));
        command.Parameters.AddWithValue(NpgsqlDbType.Integer, topK);
        var result = new List<VectorNeighbor>(topK);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            result.Add(new(reader.GetString(0), reader.GetDouble(1)));
        return result;
    }

    public async Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken)
    {
        var selected = profile ?? throw new InvalidOperationException("Vector index must be built before query planning.");
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await SetSearchSettingsAsync(connection, selected.IndexKind, cancellationToken).ConfigureAwait(false);
        if (selected.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat)
        {
            await using var force = new NpgsqlCommand("SET enable_seqscan = off", connection);
            await force.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        var sql = $"EXPLAIN (FORMAT TEXT) SELECT id,distance FROM (SELECT id,embedding <=> $1::vector AS distance FROM {Table}{Filter(mode)} ORDER BY embedding <=> $1::vector LIMIT 10) AS candidates ORDER BY distance,id";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(VectorLiteral(query.Span));
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) lines.Add(reader.GetString(0));
        var plan = string.Join('\n', lines);
        if (selected.IndexKind is VectorIndexKind.Hnsw or VectorIndexKind.IvfFlat
            && !plan.Contains(selected.IndexKind == VectorIndexKind.Hnsw ? "vectors_hnsw_idx" : "vectors_ivfflat_idx", StringComparison.Ordinal))
            throw new InvalidDataException("PostgreSQL planner did not select the requested pgvector index.");
        return plan;
    }

    public async Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"UPDATE {Table} SET embedding = $2::vector WHERE id = $1", connection);
        command.Parameters.AddWithValue(update.Id);
        command.Parameters.AddWithValue(VectorLiteral(update.Embedding.Span));
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("PostgreSQL did not update exactly one vector row.");
    }

    public async Task<VectorReadback?> ReadAsync(string id, CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"SELECT number,id,embedding::text,payload FROM {Table} WHERE id=$1", connection);
        command.Parameters.AddWithValue(id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var vector = ParseVector(reader.GetString(2));
        return new(reader.GetInt32(0), reader.GetString(1), vector.Length,
            VectorComparisonCorpus.HashVector(vector), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(reader.GetString(3)))));
    }

    public async ValueTask DisposeAsync()
    {
        if (initialized)
        {
            try
            {
                await using var connection = await source.OpenConnectionAsync();
                await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE", connection);
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            finally { initialized = false; }
        }
        await source.DisposeAsync().ConfigureAwait(false);
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (initialized) throw new InvalidOperationException("A PostgreSQL vector target can ingest once.");
        await using var connection = await source.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand($"CREATE EXTENSION IF NOT EXISTS vector; CREATE SCHEMA \"{schema}\"; CREATE TABLE {Table}(number integer NOT NULL UNIQUE,id text COLLATE \"C\" PRIMARY KEY,embedding vector(128) NOT NULL,payload text NOT NULL)", connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        initialized = true;
    }

    private async Task InsertBatchAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        IReadOnlyList<VectorDocument> batch, CancellationToken cancellationToken)
    {
        var numbers = new int[batch.Count];
        var ids = new string[batch.Count];
        var embeddings = new string[batch.Count];
        var payloads = new string[batch.Count];
        for (var index = 0; index < batch.Count; index++)
        {
            var document = batch[index];
            if (document.Embedding.Length != 128 || Encoding.UTF8.GetByteCount(document.Payload) != 1024)
                throw new InvalidDataException("The vector document violates its frozen dimension or payload length.");
            numbers[index] = document.Number;
            ids[index] = document.Id;
            embeddings[index] = VectorLiteral(document.Embedding.Span);
            payloads[index] = document.Payload;
        }
        var sql = $"INSERT INTO {Table}(number,id,embedding,payload) SELECT u.number,u.id,u.embedding::vector,u.payload FROM unnest($1::integer[],$2::text[],$3::text[],$4::text[]) AS u(number,id,embedding,payload)";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Integer, numbers);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, ids);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, embeddings);
        command.Parameters.AddWithValue(NpgsqlDbType.Array | NpgsqlDbType.Text, payloads);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SetSearchSettingsAsync(NpgsqlConnection connection, VectorIndexKind kind, CancellationToken cancellationToken)
    {
        var sql = kind switch
        {
            VectorIndexKind.Hnsw => "SET hnsw.ef_search = 200; SET hnsw.iterative_scan = strict_order",
            VectorIndexKind.IvfFlat => $"SET ivfflat.probes = {ivfProbes}; SET ivfflat.iterative_scan = relaxed_order",
            _ => null
        };
        if (sql is null) return;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Filter(VectorQueryMode mode) => mode switch
    {
        VectorQueryMode.Filtered => " WHERE number % 100 = 0",
        VectorQueryMode.Mixed => " WHERE number % 10 <> 9",
        _ => string.Empty
    };

    private static string VectorLiteral(ReadOnlySpan<float> vector)
    {
        var builder = new StringBuilder(vector.Length * 14 + 2).Append('[');
        for (var index = 0; index < vector.Length; index++)
        {
            if (index != 0) builder.Append(',');
            builder.Append(vector[index].ToString("R", CultureInfo.InvariantCulture));
        }
        return builder.Append(']').ToString();
    }

    private static float[] ParseVector(string serialized)
    {
        if (serialized.Length < 2 || serialized[0] != '[' || serialized[^1] != ']')
            throw new InvalidDataException("PostgreSQL returned an invalid pgvector value.");
        var values = serialized.AsSpan(1, serialized.Length - 2).ToString().Split(',');
        return values.Select(value => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
    }
}
