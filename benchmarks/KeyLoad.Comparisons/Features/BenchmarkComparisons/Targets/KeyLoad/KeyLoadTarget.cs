using System.Collections.Immutable;
using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares the database through its authenticated HTTP client and verifies the expected fixed voter group.</summary>
/// <param name="http">The primary HTTP client used by the KeyLoad SDK client; target disposal disposes it.</param>
/// <param name="apiKey">The credential passed to the KeyLoad SDK client for authenticated operations.</param>
/// <param name="runId">Run identifier used to derive the isolated benchmark partition.</param>
/// <param name="image">Optional database image reference included in the initial target profile.</param>
/// <param name="peers">Optional peer HTTP clients used to observe replica copies; the target disposes distinct clients.</param>
/// <param name="expectedNodes">Actual fixed benchmark voter count; the default retains the required RF3 contract.</param>
public sealed partial class KeyLoadTarget(HttpClient http, string apiKey, string runId, string? image = null,
    HttpClient[]? peers = null, int expectedNodes = 3) : IComparisonTarget
{
    private const string TransactionDomainId = "shared";
    private readonly int expectedNodeCount = ValidateExpectedNodes(expectedNodes);
    private readonly KeyLoadClient client = new(http, apiKey);
    private readonly HttpClient[] peerClients = peers is null
        ? [http]
        : peers.Contains(http, ReferenceEqualityComparer.Instance) ? peers : [.. peers, http];
    private readonly string credential = apiKey;
    private readonly PartitionRef partition = new("benchmark-" + runId, "comparison", "workload", TransactionDomainId);
    private VectorSpace space = null!;
    private int topK;
    private int graphDepth, graphVertices, graphEdges, expectedCorpusCount;
    private string[] admissionObservations = [];
    internal bool RequireIsolatedAdmission { get; init; }
    /// <summary>Gets the declared quorum, durability, API and authorization contract before actual cluster observation.</summary>
    public TargetProfile Profile { get; private set; } = CreateProfile(expectedNodes, image);
    /// <summary>Reports support for document, vector, queue, graph, and event-stream comparison scenarios.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for a scenario implemented by this target; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.VectorExact
        or Scenario.QueueCycle or Scenario.GraphNeighbors or Scenario.GraphTraverse or Scenario.StreamAppend or Scenario.StreamRead
        or Scenario.DocumentUpdate or Scenario.DocumentDelete;
    /// <summary>Requires the expected voters and process-durable quorum writes, seeds the corpus and observes every copy.</summary>
    /// <param name="dataset">The deterministic documents, vectors, graph edges, and workload options to provision.</param>
    /// <param name="cancellationToken">A token that cancels status checks and database operations.</param>
    /// <returns>A task that completes after seeding and replica observation.</returns>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        admissionObservations = await KeyLoadAdmissionObserver.ObserveAsync(RequireIsolatedAdmission,
            peerClients, credential, cancellationToken).ConfigureAwait(false);
        var status = KeyLoadClientResults.Success(await client.StatusAsync(cancellationToken), "Status");
        if (status.Voters != expectedNodeCount || status.Durability != DurabilityProfile.QuorumProcessDurable)
        {
            throw new ComparisonFailureException(expectedNodeCount == 3 ? Rf3Required : BenchmarkTopologyMismatch);
        }

        var scaled = dataset.Settings is ScaledComparisonProfile;
        ConfigureCorpus(dataset, scaled);
        await ConfigureResourcesAsync(scaled, cancellationToken);
        await SeedDocumentsAsync(dataset, scaled, cancellationToken);
        await SeedGraphAsync(dataset, scaled, cancellationToken);
        if (!scaled && dataset is BenchmarkDataset control)
        {
            await KeyLoadEventOperations.SeedAsync(client, partition, control, cancellationToken);
        }
        await ObserveCopiesAsync(cancellationToken);
    }

    private void ConfigureCorpus(IComparisonCorpus dataset, bool scaled)
    {
        if (scaled)
        {
            Profile = Profile with { ReadContract = "authenticated primary document reads; S1 seeds documents only, with no vector, queue, graph or event records" };
        }
        expectedCorpusCount = dataset.Documents.Count;
        space = scaled ? null! : new("comparison", dataset.Settings.Dimensions, DistanceMetric.Cosine, "seeded-float32", "1");
        topK = dataset.Settings.TopK;
        graphDepth = dataset.Settings.GraphDepth;
        graphVertices = dataset.GraphVertexCount;
        graphEdges = scaled ? 0 : Math.Max(1, dataset.Edges.Count);
    }

    private async Task ConfigureResourcesAsync(bool scaled, CancellationToken cancellationToken)
    {
        var resources = scaled ? new[] { ("documents", ResourceKind.Collection) }
            : new[] { ("documents", ResourceKind.Collection), ("jobs", ResourceKind.WorkQueue), ("links", ResourceKind.Graph),
                (KeyLoadEventOperations.EventsName, ResourceKind.StreamSet) };
        foreach (var (name, kind) in resources)
        {
            KeyLoadClientResults.Success(await client.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(name, kind, partition.TransactionDomainId)), cancellationToken), "Configure:" + name);
        }
    }

    private async Task SeedDocumentsAsync(IComparisonCorpus dataset, bool scaled, CancellationToken cancellationToken)
    {
        if (scaled)
        {
            await KeyLoadScaledCorpusSeeder.SeedAsync(client, partition, dataset.Documents, cancellationToken);
            return;
        }
        foreach (var document in dataset.Documents)
        {
            ImmutableArray<Mutation> mutations = [new PutDocument("documents", document.Id, document.Json, 0),
                new PutVector("documents", document.Id, "/embedding", document.Vector, space, 1)];
            KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, mutations), cancellationToken), "SeedDocuments");
        }
    }

    private async Task SeedGraphAsync(IComparisonCorpus dataset, bool scaled, CancellationToken cancellationToken)
    {
        if (scaled)
        {
            return;
        }
        foreach (var batch in dataset.Edges.Chunk(100))
        {
            var mutations = batch.Select(edge => (Mutation)new UpsertEdge("links", edge.Id,
                new(partition, "documents", edge.From), new(partition, "documents", edge.To), "links", ExpectedRevision: 0));
            KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, [.. mutations]), cancellationToken), "SeedGraph");
        }
    }
    /// <summary>Opens a session that executes supported operations through the SDK client.</summary>
    /// <param name="cancellationToken">A token accepted for the common target contract; this implementation creates the session without I/O.</param>
    /// <returns>A comparison session bound to this target and its benchmark partition.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => Task.FromResult<IComparisonSession>(new KeyLoadComparisonSession(client, partition, space, topK,
            graphDepth, graphVertices, graphEdges, expectedCorpusCount));
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
