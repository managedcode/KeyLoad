using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares document and directed graph operations against an isolated Neo4j Community label on one node.</summary>
/// <param name="http">The authenticated Neo4j Query API client; the target disposes it.</param>
/// <param name="runId">Guid-formatted run identifier used to isolate node labels and constraints.</param>
/// <param name="image">Neo4j image reference recorded in the target profile.</param>
public sealed class Neo4jTarget(HttpClient http, string runId, string image) : IComparisonTarget
{
    private const string DataProperty = "data";
    private const string ValuesProperty = "values";
    private const string CommunityEdition = "community";
    private const string CommunityRequired = "Neo4jCommunityEditionRequired";
    private const string SingleCommunityState = "single native Community node";
    private readonly string label = "Benchmark_" + Guid.Parse(runId).ToString("N");
    private bool ownsConstraint;
    private int depth;
    /// <summary>Gets the observed Neo4j version and declared single-node, local-transaction, and query profile.</summary>
    public TargetProfile Profile { get; private set; } = new("Neo4j", "unverified", "Community; single node; heap 512 MiB, page cache 256 MiB",
        "local committed transaction; no synchronous replicas; durability not fault-qualified", "committed primary; indexed IDs and bounded directed reachability",
        "Cypher Query API v2 / HTTP JSON", "Neo4j admin; no row/field policy", image);
    /// <summary>Reports support for point reads, document writes, and directed graph neighbor or traversal queries.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for a supported scenario; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate
        or Scenario.DocumentDelete or Scenario.GraphNeighbors or Scenario.GraphTraverse;

    /// <summary>Reads the server version, creates an isolated uniqueness constraint, seeds documents and edges, and waits for indexes.</summary>
    /// <param name="dataset">The deterministic documents and directed graph edges to seed.</param>
    /// <param name="cancellationToken">A token that cancels HTTP queries and setup operations.</param>
    /// <returns>A task that completes after index readiness.</returns>
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        depth = dataset.Options.GraphDepth;
        using var version = await QueryAsync("CALL dbms.components() YIELD name,versions,edition WHERE name='Neo4j Kernel' RETURN versions[0],edition", null, cancellationToken);
        var row = Rows(version).EnumerateArray().Single();
        var edition = row[1].GetString();
        if (!string.Equals(edition, CommunityEdition, StringComparison.OrdinalIgnoreCase))
        {
            throw new ComparisonFailureException(CommunityRequired);
        }
        Profile = Profile with
        {
            Version = row[0].GetString() + "; " + edition,
            Cluster = new(1, 1, SingleCommunityState, [CommunityEdition])
        };
        using (var constraint = await QueryAsync($"CREATE CONSTRAINT {label}_id FOR (n:{label}) REQUIRE n.id IS UNIQUE", null, cancellationToken))
        {
            Neo4jQueryProtocol.ValidateConstraintCreation(constraint.RootElement);
            ownsConstraint = true;
        }

        foreach (var batch in dataset.Documents.Chunk(256))
        {
            await ExecuteAsync($"UNWIND $documents AS d CREATE (n:{label} {{id:d.id,json:d.json}})",
                new { documents = batch.Select(document => new { id = document.Id, json = document.Json }).ToArray() }, cancellationToken);
        }

        foreach (var batch in dataset.Edges.Chunk(256))
        {
            await ExecuteAsync($"UNWIND $edges AS e MATCH (a:{label} {{id:e.from}}),(b:{label} {{id:e.to}}) CREATE (a)-[:LINKS]->(b)",
                new { edges = batch.Select(edge => new { from = edge.From, to = edge.To }).ToArray() }, cancellationToken);
        }

        await ExecuteAsync("CALL db.awaitIndexes(30)", null, cancellationToken);
    }

    private static JsonElement Rows(JsonDocument response) => response.RootElement.GetProperty(DataProperty).GetProperty(ValuesProperty);
    private async Task<JsonDocument> QueryAsync(string statement, object? parameters, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("db/neo4j/query/v2", new { statement, parameters = parameters ?? new { }, maxExecutionTime = 30 }, cancellationToken);
        Neo4jQueryProtocol.RequireQueryStatus((int)response.StatusCode);
        JsonDocument? json = null;
        try
        {
            json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            Neo4jQueryProtocol.ValidateResponse((int)response.StatusCode, json.RootElement);
            return json;
        }
        catch (JsonException)
        {
            json?.Dispose();
            throw Neo4jQueryProtocol.Invalid();
        }
        catch (Exception)
        {
            json?.Dispose();
            throw;
        }
    }
    private async Task ExecuteAsync(string statement, object? parameters, CancellationToken cancellationToken)
    { using var response = await QueryAsync(statement, parameters, cancellationToken); }
    /// <summary>Opens a session that runs queries through this target’s Neo4j client.</summary>
    /// <param name="cancellationToken">A token accepted for the common target contract; session construction does not perform I/O.</param>
    /// <returns>A comparison session bound to this target.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken) => Task.FromResult<IComparisonSession>(new Session(this));
    /// <summary>Deletes this run’s nodes and uniqueness constraint, then disposes the owned HTTP client.</summary>
    /// <returns>A value task that completes after cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            if (ownsConstraint)
            {
                await ExecuteAsync($"MATCH (n:{label}) DETACH DELETE n", null, timeout.Token);
                await ExecuteAsync($"DROP CONSTRAINT {label}_id IF EXISTS", null, timeout.Token);
            }
        }
        finally { http.Dispose(); }
    }

    /// <summary>Executes one comparison session’s document and directed graph queries.</summary>
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
                case Scenario.PointRead:
                    return new(Document: await ReadAsync(document, cancellationToken));
                case Scenario.DocumentWrite:
                case Scenario.DocumentUpdate:
                case Scenario.DocumentDelete:
                    return await Neo4jMutationOperations.ExecuteAsync(target.QueryAsync, target.label, scenario, document, cancellationToken);
                case Scenario.GraphNeighbors:
                case Scenario.GraphTraverse:
                    var hops = scenario == Scenario.GraphNeighbors ? 1 : target.depth;
                    using (var response = await target.QueryAsync($"MATCH (a:{target.label} {{id:$id}})-[:LINKS*1..{hops}]->(b:{target.label}) WHERE b.id<>$id RETURN DISTINCT b.id ORDER BY b.id", new { id = document.Id }, cancellationToken))
                    {
                        return new(Vertices: ImmutableCollectionsMarshal.AsImmutableArray(Rows(response).EnumerateArray()
                            .Select(row => row[0].GetString()!).ToArray()));
                    }

                default:
                    throw new NotSupportedException();
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
