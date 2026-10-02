using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares the database through its authenticated HTTP client while requiring the configured three-voter RF3 cluster.</summary>
/// <param name="http">The primary HTTP client used by the KeyLoad SDK client; target disposal disposes it.</param>
/// <param name="apiKey">The credential passed to the KeyLoad SDK client for authenticated operations.</param>
/// <param name="runId">Run identifier used to derive the isolated benchmark partition.</param>
/// <param name="image">Optional database image reference included in the initial target profile.</param>
/// <param name="peers">Optional peer HTTP clients used to observe replica copies; the target disposes distinct clients.</param>
public sealed partial class KeyLoadTarget(HttpClient http, string apiKey, string runId, string? image = null,
    HttpClient[]? peers = null) : IComparisonTarget
{
    private const string TransactionDomainId = "shared";
    private readonly KeyLoadClient client = new(http, apiKey);
    private readonly HttpClient[] peerClients = peers is null
        ? [http]
        : peers.Contains(http, ReferenceEqualityComparer.Instance) ? peers : [.. peers, http];
    private readonly string credential = apiKey;
    private readonly PartitionRef partition = new("benchmark-" + runId, "comparison", "workload", TransactionDomainId);
    private VectorSpace space = null!;
    private int topK;
    private int graphDepth, graphVertices, graphEdges;
    /// <summary>Gets the declared RF3, process-durable, API, and authorization profile for this target.</summary>
    public TargetProfile Profile { get; private set; } = new("KeyLoad", "0.1.0-dev", "3 voters, RF3, one physical shard; all processes on one host",
        "QuorumProcessDurable; process-kill qualified, power-loss unqualified", "strong quorum barrier; graph returns vertices and edges, projected to IDs", "HTTP JSON", "authenticated root, all grants", image);
    /// <summary>Reports support for document, vector, queue, graph, and event-stream comparison scenarios.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for a scenario implemented by this target; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.VectorExact
        or Scenario.QueueCycle or Scenario.GraphNeighbors or Scenario.GraphTraverse or Scenario.StreamAppend or Scenario.StreamRead;
    /// <summary>Requires three voters and process-durable quorum writes, provisions the benchmark resources, seeds the corpus, and observes replica copies.</summary>
    /// <param name="dataset">The deterministic documents, vectors, graph edges, and workload options to provision.</param>
    /// <param name="cancellationToken">A token that cancels status checks and database operations.</param>
    /// <returns>A task that completes after seeding and replica observation.</returns>
    public async Task InitializeAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var status = KeyLoadClientResults.Success(await client.StatusAsync(cancellationToken), "Status");
        if (status.Voters != 3 || status.Durability != DurabilityProfile.QuorumProcessDurable)
        {
            throw new ComparisonFailureException("KeyLoadRf3Required");
        }

        space = new("comparison", dataset.Options.Dimensions, DistanceMetric.Cosine, "seeded-float32", "1");
        topK = dataset.Options.TopK;
        graphDepth = dataset.Options.GraphDepth;
        graphVertices = dataset.GraphVertexCount;
        graphEdges = Math.Max(1, dataset.Edges.Length);
        foreach (var (name, kind) in new[] { ("documents", ResourceKind.Collection), ("jobs", ResourceKind.WorkQueue), ("links", ResourceKind.Graph), (KeyLoadEventOperations.EventsName, ResourceKind.StreamSet) })
        {
            KeyLoadClientResults.Success(await client.ConfigureResourceAsync(Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, new(name, kind, partition.TransactionDomainId)), cancellationToken), "Configure:" + name);
        }

        foreach (var document in dataset.Documents)
        {
            KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition,
                [new PutDocument("documents", document.Id, document.Json, 0), new PutVector("documents", document.Id, "/embedding", document.Vector, space, 1)]), cancellationToken), "SeedDocuments");
        }

        foreach (var batch in dataset.Edges.Chunk(100))
        {
            KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, [.. batch.Select(edge => (Mutation)new UpsertEdge("links", edge.Id,
                new(partition, "documents", edge.From), new(partition, "documents", edge.To), "links", ExpectedRevision: 0))]), cancellationToken), "SeedGraph");
        }

        await KeyLoadEventOperations.SeedAsync(client, partition, dataset, cancellationToken);
        await ObserveCopiesAsync(cancellationToken);
    }
    /// <summary>Opens a session that executes supported operations through the SDK client.</summary>
    /// <param name="cancellationToken">A token accepted for the common target contract; this implementation creates the session without I/O.</param>
    /// <returns>A comparison session bound to this target and its benchmark partition.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => Task.FromResult<IComparisonSession>(new KeyLoadComparisonSession(client, partition, space, topK,
            graphDepth, graphVertices, graphEdges));
    /// <summary>Disposes the distinct HTTP clients owned by this target.</summary>
    /// <returns>A value task that completes after client disposal.</returns>
    public ValueTask DisposeAsync()
    {
        foreach (var peer in peerClients.Distinct())
        {
            peer.Dispose();
        }

        return ValueTask.CompletedTask;
    }

}
