using System.Net.Http.Json;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

public sealed class Neo4jTarget(HttpClient http, string runId, string image) : IComparisonTarget
{
    private readonly string label = "Benchmark_" + Guid.Parse(runId).ToString("N");
    private int depth;
    public TargetProfile Profile { get; private set; } = new("Neo4j", "unverified", "Community; single node; heap 512 MiB, page cache 256 MiB",
        "local committed transaction; no synchronous replicas; durability not fault-qualified", "committed primary; indexed IDs and bounded directed reachability",
        "Cypher Query API v2 / HTTP JSON", "Neo4j admin; no row/field policy", image);
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.GraphNeighbors or Scenario.GraphTraverse;

    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        depth = dataset.Options.GraphDepth;
        using var version = await QueryAsync("CALL dbms.components() YIELD name,versions,edition WHERE name='Neo4j Kernel' RETURN versions[0],edition", null, cancellationToken);
        var row = Rows(version).EnumerateArray().Single();
        Profile = Profile with { Version = row[0].GetString() + "; " + row[1].GetString() };
        await ExecuteAsync($"CREATE CONSTRAINT {label}_id FOR (n:{label}) REQUIRE n.id IS UNIQUE", null, cancellationToken);
        foreach (var batch in dataset.Documents.Chunk(256))
            await ExecuteAsync($"UNWIND $documents AS d CREATE (n:{label} {{id:d.id,json:d.json}})",
                new { documents = batch.Select(document => new { id = document.Id, json = document.Json }).ToArray() }, cancellationToken);
        foreach (var batch in dataset.Edges.Chunk(256))
            await ExecuteAsync($"UNWIND $edges AS e MATCH (a:{label} {{id:e.from}}),(b:{label} {{id:e.to}}) CREATE (a)-[:LINKS]->(b)",
                new { edges = batch.Select(edge => new { from = edge.From, to = edge.To }).ToArray() }, cancellationToken);
        await ExecuteAsync("CALL db.awaitIndexes(30)", null, cancellationToken);
    }

    private static JsonElement Rows(JsonDocument response) => response.RootElement.GetProperty("data").GetProperty("values");
    private async Task<JsonDocument> QueryAsync(string statement, object? parameters, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("db/neo4j/query/v2", new { statement, parameters = parameters ?? new { }, maxExecutionTime = 30 }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (json.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() != 0)
        {
            // Query API returns HTTP 202 for query failures too. Never log server messages or credentials.
            var code = errors[0].GetProperty("code").GetString(); json.Dispose();
            throw new ComparisonFailure("Neo4j:" + code);
        }
        return json;
    }
    private async Task ExecuteAsync(string statement, object? parameters, CancellationToken cancellationToken)
    { using var response = await QueryAsync(statement, parameters, cancellationToken); }
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken) => Task.FromResult<IComparisonSession>(new Session(this));
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await ExecuteAsync($"MATCH (n:{label}) DETACH DELETE n", null, timeout.Token);
            await ExecuteAsync($"DROP CONSTRAINT {label}_id IF EXISTS", null, timeout.Token);
        }
        finally { http.Dispose(); }
    }

    private sealed class Session(Neo4jTarget target) : IComparisonSession
    {
        public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        {
            using var response = await target.QueryAsync($"MATCH (n:{target.label} {{id:$id}}) RETURN n.json", new { id = document.Id }, cancellationToken);
            var rows = Rows(response);
            return rows.GetArrayLength() == 0 ? null : new(document.Id, rows[0][0].GetString()!);
        }
        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            switch (scenario)
            {
                case Scenario.PointRead: return new(Document: await ReadAsync(document, cancellationToken));
                case Scenario.DocumentWrite:
                    await target.ExecuteAsync($"CREATE (n:{target.label} {{id:$id,json:$json}})", new { id = document.Id, json = document.Json }, cancellationToken);
                    return new();
                case Scenario.GraphNeighbors:
                case Scenario.GraphTraverse:
                    var hops = scenario == Scenario.GraphNeighbors ? 1 : target.depth;
                    using (var response = await target.QueryAsync($"MATCH (a:{target.label} {{id:$id}})-[:LINKS*1..{hops}]->(b:{target.label}) WHERE b.id<>$id RETURN DISTINCT b.id ORDER BY b.id", new { id = document.Id }, cancellationToken))
                        return new(Vertices: Rows(response).EnumerateArray().Select(row => row[0].GetString()!).ToArray());
                default: throw new NotSupportedException();
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
