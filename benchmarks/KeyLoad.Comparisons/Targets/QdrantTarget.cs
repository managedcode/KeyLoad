using System.Net.Http.Json;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

public sealed class QdrantTarget(HttpClient http, string runId, string image) : IComparisonTarget
{
    private readonly HttpClient client = http;
    private readonly string collection = "keyload_benchmark_" + Guid.Parse(runId).ToString("N");
    private int topK;
    public TargetProfile Profile { get; private set; } = new("Qdrant", "unverified", "single node, replication_factor=1",
        "wait=true seed upserts; no RF3 data quorum", "exact=true; seeded collection static during queries", "HTTP JSON", "Aspire API key; no row/field policy", image);
    public bool Supports(Scenario scenario) => scenario == Scenario.VectorExact;
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        topK = dataset.Options.TopK;
        using (var root = await client.GetFromJsonAsync<JsonDocument>("/", cancellationToken))
            Profile = Profile with { Version = root!.RootElement.GetProperty("version").GetString()! };
        using (var create = await client.PutAsJsonAsync($"/collections/{collection}", new {
            vectors = new { size = dataset.Options.Dimensions, distance = "Cosine" }, replication_factor = 1,
            hnsw_config = new { m = 0 } }, cancellationToken)) create.EnsureSuccessStatusCode();
        foreach (var batch in dataset.Documents.Chunk(64))
        {
            var points = batch.Select(document => new { id = document.Number + 1, vector = document.Vector,
                payload = new { id = document.Id, document = JsonSerializer.Deserialize<JsonElement>(document.Json) } });
            using var response = await client.PutAsJsonAsync($"/collections/{collection}/points?wait=true", new { points }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken) => Task.FromResult<IComparisonSession>(new Session(this));
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { using var response = await client.DeleteAsync($"/collections/{collection}", timeout.Token); }
        finally { client.Dispose(); }
    }
    private sealed class Session(QdrantTarget target) : IComparisonSession
    {
        public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            if (scenario != Scenario.VectorExact) throw new NotSupportedException();
            using var response = await target.client.PostAsJsonAsync($"/collections/{target.collection}/points/query",
                new { query = document.Vector, limit = target.topK, @params = new { exact = true }, with_payload = true, with_vector = false }, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            return new(Neighbors: result!.RootElement.GetProperty("result").GetProperty("points").EnumerateArray().Select(point =>
            {
                var payload = point.GetProperty("payload");
                return new FoundDocument(payload.GetProperty("id").GetString()!, payload.GetProperty("document").GetRawText());
            }).ToArray());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
