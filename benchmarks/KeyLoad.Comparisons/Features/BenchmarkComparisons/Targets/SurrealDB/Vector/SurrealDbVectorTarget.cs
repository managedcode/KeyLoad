using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Runs SurrealDB's native brute-force and HNSW vector paths over its persistent RocksDB backend.</summary>
public sealed class SurrealDbVectorTarget(HttpClient http, string image, string runId) : IVectorComparisonTarget
{
    private const string Table = "vector_doc";
    private const string Database = "main";
    private const string Namespace = "main";
    private const string ApiPath = "sql";
    private const string InvalidResponse = "SurrealDbInvalidResponse";
    private const string RequestFailed = "SurrealDbRequestFailed";
    private const string IndexNamePrefix = "keyload_hnsw_";
    private const int BatchSize = 24;
    private const int ReadbackBatchSize = 256;
    private const int HnswM = 16;
    private const int HnswEfc = 200;
    private const int HnswEf = 200;
    private static readonly TimeSpan IndexBuildTimeout = TimeSpan.FromMinutes(20);
    private readonly string table = Table + "_" + Guid.Parse(runId).ToString("N");
    private readonly string index = IndexNamePrefix + Guid.Parse(runId).ToString("N");
    private int corpusCount;
    private string databaseVersion = "unverified";
    private bool ownsData;
    private bool ownsIndex;

    /// <inheritdoc />
    public string Name => "SurrealDB";

    /// <inheritdoc />
    public TargetProfile Profile { get; private set; } = new("SurrealDB", "unverified",
        "SurrealDB Community; one persistent RocksDB node", "single-node acknowledged write; commit durability not fault-qualified",
        "single-node committed reads", "SurrealDB HTTP /sql JSON", "root Basic authentication", image)
    {
        Cluster = new(1, 1, "single SurrealDB Community node; no replication configured", ["persistent RocksDB path"])
    };

    /// <inheritdoc />
    public bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode)
        => indexKind is VectorIndexKind.Exact or VectorIndexKind.Hnsw && Enum.IsDefined(queryMode);

    /// <inheritdoc />
    public async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await VerifyServerAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteAsync($"DEFINE TABLE IF NOT EXISTS {table} SCHEMALESS;", cancellationToken).ConfigureAwait(false);
        ownsData = true;

        var batch = new List<VectorDocument>(BatchSize);
        var count = 0;
        await foreach (var document in documents.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (document.Embedding.Length == 0 || document.Number < 0 || !document.Id.StartsWith('v'))
                throw new InvalidDataException("SurrealDbInvalidSeedDocument");
            batch.Add(document);
            count++;
            if (batch.Count == BatchSize)
            {
                await IngestBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }
        if (batch.Count != 0) await IngestBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        corpusCount = count;
        Profile = Profile with { Version = databaseVersion + "; HTTP /sql; RocksDB persistent backend" };
        return count;
    }

    /// <inheritdoc />
    public async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Supports(profile.IndexKind, profile.QueryMode)) throw new NotSupportedException("SurrealDB profile is unavailable.");
        ProfileIndex = profile.IndexKind;
        if (profile.IndexKind == VectorIndexKind.Exact)
            return new(VectorIndexKind.Exact, "no ANN index; SurrealDB brute-force KNN", new Dictionary<string, string>(), 0);
        if (profile.IndexKind != VectorIndexKind.Hnsw)
            throw new NotSupportedException("SurrealDB supports only exact and native HNSW profiles.");

        var definition = $"DEFINE INDEX {index} ON TABLE {table} FIELDS embedding HNSW DIMENSION {profile.Dimensions} TYPE F32 DIST COSINE EFC {HnswEfc} M {HnswM} CONCURRENTLY;";
        var timer = System.Diagnostics.Stopwatch.StartNew();
        await ExecuteAsync(definition, cancellationToken).ConfigureAwait(false);
        ownsIndex = true;
        var deadline = TimeProvider.System.GetUtcNow() + IndexBuildTimeout;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await QueryAsync($"INFO FOR INDEX {index} ON TABLE {table};", cancellationToken).ConfigureAwait(false);
            var indexInfo = SingleResult(response.RootElement);
            if (!indexInfo.TryGetProperty("building", out var building) || building.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(InvalidResponse);
            var state = ReadRequiredString(building, "status");
            if (state.Equals("ready", StringComparison.OrdinalIgnoreCase)) break;
            if (state is "failed" or "error" || TimeProvider.System.GetUtcNow() >= deadline)
                throw new InvalidDataException("SurrealDbHnswBuildDidNotBecomeReady");
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }
        timer.Stop();
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["algorithm"] = "HNSW", ["dimension"] = profile.Dimensions.ToString(CultureInfo.InvariantCulture),
            ["distance"] = "COSINE", ["efConstruction"] = HnswEfc.ToString(CultureInfo.InvariantCulture),
            ["m"] = HnswM.ToString(CultureInfo.InvariantCulture), ["efSearch"] = HnswEf.ToString(CultureInfo.InvariantCulture)
        };
        return new(VectorIndexKind.Hnsw, definition, parameters, timer.Elapsed.TotalMilliseconds);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<VectorReadback> ReadbackAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seen = 0;
        var after = -1;
        while (true)
        {
            using var response = await QueryAsync($"SELECT number, id, embedding, payload FROM {table} WHERE number > {after} ORDER BY number LIMIT {ReadbackBatchSize};",
                cancellationToken).ConfigureAwait(false);
            var rows = SingleResult(response.RootElement);
            if (rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException(InvalidResponse);
            if (rows.GetArrayLength() == 0) break;
            foreach (var row in rows.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var number = row.GetProperty("number").GetInt32();
                var id = ReadRecordId(row.GetProperty("id"));
                var embedding = ReadVector(row.GetProperty("embedding"));
                var payload = ReadRequiredString(row, "payload");
                after = number;
                seen++;
                yield return new(number, id, embedding.Length, VectorComparisonCorpus.HashVector(embedding),
                    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload))));
            }
        }
        if (seen != corpusCount) throw new InvalidDataException("SurrealDbReadbackCountMismatch");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken cancellationToken)
    {
        var filter = FilterClause(mode);
        var operatorParameters = ProfileIndex == VectorIndexKind.Hnsw ? $"{topK}, {HnswEf}" : $"{topK}, COSINE";
        var sql = $"SELECT id, vector::distance::knn() AS distance FROM {table} WHERE embedding <|{operatorParameters}|> {FormatVector(query.Span)}{filter} ORDER BY distance, id LIMIT {topK};";
        using var response = await QueryAsync(sql, cancellationToken).ConfigureAwait(false);
        var result = SingleResult(response.RootElement);
        if (result.ValueKind != JsonValueKind.Array) throw new InvalidDataException(InvalidResponse);
        return result.EnumerateArray().Select(row => new VectorNeighbor(ReadRequiredString(row, "id"),
            ReadRequiredDouble(row, "distance"))).ToArray();
    }

    /// <inheritdoc />
    public async Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken)
    {
        var filter = FilterClause(mode);
        var operatorParameters = ProfileIndex == VectorIndexKind.Hnsw ? $"{10}, {HnswEf}" : "10, COSINE";
        var sql = $"EXPLAIN FULL SELECT id, vector::distance::knn() AS distance FROM {table} WHERE embedding <|{operatorParameters}|> {FormatVector(query.Span)}{filter} ORDER BY distance, id LIMIT 10;";
        using var response = await QueryAsync(sql, cancellationToken).ConfigureAwait(false);
        var plan = SingleResult(response.RootElement).ToString();
        if (ProfileIndex == VectorIndexKind.Hnsw && !plan.Contains(index, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SurrealDbHnswPlanNotProven");
        return plan;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken)
        => await ExecuteAsync($"UPDATE {table}:{RecordKey(update.Id)} SET embedding = {FormatVector(update.Embedding.Span)};",
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<VectorReadback?> ReadAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await QueryAsync($"SELECT number, id, embedding, payload FROM {table}:{RecordKey(id)};", cancellationToken)
            .ConfigureAwait(false);
        var result = SingleResult(response.RootElement);
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0) return null;
        var row = result[0];
        var vector = ReadVector(row.GetProperty("embedding"));
        return new(row.GetProperty("number").GetInt32(), ReadRecordId(row.GetProperty("id")), vector.Length,
            VectorComparisonCorpus.HashVector(vector), Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ReadRequiredString(row, "payload")))));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ownsData)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                if (ownsIndex) await ExecuteAsync($"REMOVE INDEX {index} ON TABLE {table};", timeout.Token).ConfigureAwait(false);
                await ExecuteAsync($"DELETE FROM {table}; REMOVE TABLE {table};", timeout.Token).ConfigureAwait(false);
            }
        }
        finally { http.Dispose(); }
    }

    private VectorIndexKind ProfileIndex { get; set; } = VectorIndexKind.Exact;

    private async Task VerifyServerAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("version", cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new ComparisonFailureException(RequestFailed);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            databaseVersion = ReadRequiredString(json.RootElement, "version");
            if (databaseVersion != "3.2.4") throw new InvalidDataException("SurrealDbServerVersionMismatch");
        }
        catch (JsonException error) { throw new ComparisonFailureException(InvalidResponse, error); }
    }

    private async Task IngestBatchAsync(List<VectorDocument> batch, CancellationToken cancellationToken)
    {
        var sql = new StringBuilder(batch.Count * 12_000);
        foreach (var document in batch)
        {
            sql.Append("CREATE ONLY ").Append(table).Append(':').Append(RecordKey(document.Id))
                .Append(" SET number = ").Append(document.Number.ToString(CultureInfo.InvariantCulture))
                .Append(", embedding = ").Append(FormatVector(document.Embedding.Span))
                .Append(", payload = ").Append(Quote(document.Payload)).Append(';');
        }
        await ExecuteAsync(sql.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> QueryAsync(string sql, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiPath)
        {
            Content = new StringContent(sql, Encoding.UTF8, "text/plain")
        };
        request.Headers.TryAddWithoutValidation("Surreal-NS", Namespace);
        request.Headers.TryAddWithoutValidation("Surreal-DB", Database);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new ComparisonFailureException(RequestFailed);
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            ValidateResponse(document.RootElement);
            return document;
        }
        catch (JsonException error) { throw new ComparisonFailureException(InvalidResponse, error); }
    }

    private async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
    {
        using var response = await QueryAsync(sql, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateResponse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) throw new InvalidDataException(InvalidResponse);
        foreach (var statement in root.EnumerateArray())
        {
            var status = ReadRequiredString(statement, "status");
            if (!status.Equals("OK", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("SurrealDbStatementFailed");
            _ = statement.GetProperty("result");
        }
    }

    private static JsonElement SingleResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 1) throw new InvalidDataException(InvalidResponse);
        return root[0].GetProperty("result");
    }

    private static string FilterClause(VectorQueryMode mode) => mode switch
    {
        VectorQueryMode.Plain => string.Empty,
        VectorQueryMode.Filtered => " AND number % 100 = 0",
        VectorQueryMode.Mixed => " AND number % 10 != 9",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static string FormatVector(ReadOnlySpan<float> vector)
    {
        var result = new StringBuilder(vector.Length * 12 + 2).Append('[');
        for (var index = 0; index < vector.Length; index++)
        {
            if (!float.IsFinite(vector[index])) throw new InvalidDataException("SurrealDbInvalidVector");
            if (index != 0) result.Append(',');
            result.Append(vector[index].ToString("R", CultureInfo.InvariantCulture));
        }
        return result.Append(']').ToString();
    }

    private static string RecordKey(string id)
        => id.Length == 10 && id[0] == 'v' && id.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0
            ? "v" + id[1..]
            : throw new InvalidDataException("SurrealDbInvalidRecordId");

    private static string Quote(string value) => JsonSerializer.Serialize(value);
    private static float[] ReadVector(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException(InvalidResponse);
        var vector = new float[value.GetArrayLength()];
        var index = 0;
        foreach (var component in value.EnumerateArray())
        {
            var number = component.GetSingle();
            if (!float.IsFinite(number)) throw new InvalidDataException(InvalidResponse);
            vector[index++] = number;
        }
        return vector;
    }

    private static string ReadRequiredString(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()! : throw new InvalidDataException(InvalidResponse);

    private static double ReadRequiredDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var property) && property.TryGetDouble(out var value) && double.IsFinite(value)
            ? value : throw new InvalidDataException(InvalidResponse);

    private string ReadRecordId(JsonElement id)
    {
        if (id.ValueKind != JsonValueKind.String) throw new InvalidDataException(InvalidResponse);
        var value = id.GetString()!;
        var separator = value.LastIndexOf(':');
        if (separator < 0 || !value.AsSpan(0, separator).SequenceEqual(table)) throw new InvalidDataException(InvalidResponse);
        return "v" + value[(separator + 1)..];
    }
}
