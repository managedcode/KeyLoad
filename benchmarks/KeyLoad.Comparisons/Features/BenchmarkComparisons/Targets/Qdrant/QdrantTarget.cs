using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares exact vector search against a run-specific Qdrant collection and verifies its configured native peers.</summary>
public sealed class QdrantTarget : IComparisonTarget, IVectorComparisonTarget
{
    private const string RunIdentityFormat = "N";
    private const int CleanupTimeoutSeconds = 10;

    private const string CollectionPrefix = "keyload_benchmark_";
    private const string ResultProperty = "result";
    private const string PointsProperty = "points";
    private const string PayloadProperty = "payload";
    private const string IdProperty = "id";
    private const string DocumentProperty = "document";
    private readonly HttpClient client;
    private readonly HttpClient[] nodeClients;
    private readonly HttpClient[] ownedClients;
    private readonly string collection;
    private readonly string image;
    private readonly ComparisonTopology topology;
    private readonly string seedSuffix;
    private readonly string querySuffix;
    private bool collectionCreationAttempted;
    private int topK;
    private QdrantVectorOperations? vectorOperations;
    private readonly IOptions<NativeComparisonExecutionOptions>? executionOptions;

    private QdrantVectorOperations Vectors => vectorOperations ??= new(client, nodeClients, collection, image, topology,
        executionOptions ?? throw new InvalidOperationException(QdrantVectorProtocol.MissingExecutionPolicy));
    string IVectorComparisonTarget.Name => QdrantVectorProtocol.TargetName;
    bool IVectorComparisonTarget.Supports(VectorIndexKind indexKind, VectorQueryMode queryMode)
        => indexKind is VectorIndexKind.Exact or VectorIndexKind.Hnsw && Enum.IsDefined(queryMode);
    async Task<int> IVectorComparisonTarget.IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken token)
    {
        var loaded = await Vectors.IngestAsync(documents, token);
        Profile = Vectors.Profile;
        return loaded;
    }
    async Task<VectorIndexReceipt> IVectorComparisonTarget.BuildIndexAsync(VectorComparisonProfile profile, CancellationToken token)
    {
        var receipt = await Vectors.BuildIndexAsync(profile, token);
        Profile = Profile with { ReadContract = QdrantVectorProtocol.NativeReadPrefix + profile.IndexKind
            + QdrantVectorProtocol.CosineReadDescription + profile.QueryMode + QdrantVectorProtocol.PredicateReadDescription
            + QdrantNativePolicy.ReadContract(topology) };
        return receipt;
    }
    IAsyncEnumerable<VectorReadback> IVectorComparisonTarget.ReadbackAsync(CancellationToken token) => Vectors.ReadbackAsync(token);
    Task<IReadOnlyList<VectorNeighbor>> IVectorComparisonTarget.SearchAsync(ReadOnlyMemory<float> query, int count, VectorQueryMode mode, CancellationToken token)
        => Vectors.SearchAsync(query, count, mode, token);
    Task<string> IVectorComparisonTarget.ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken token)
        => Vectors.ExplainAsync(mode, token);
    Task IVectorComparisonTarget.UpdateAsync(VectorUpdate update, CancellationToken token) => Vectors.UpdateAsync(update, token);
    Task<VectorReadback?> IVectorComparisonTarget.ReadAsync(string id, CancellationToken token) => Vectors.ReadAsync(id, token);

    /// <summary>Creates a Qdrant vector-search target and records the HTTP clients it owns for disposal.</summary>
    /// <param name="http">The collection and query client; the target disposes it.</param>
    /// <param name="runId">Guid-formatted run identifier used to isolate the collection.</param>
    /// <param name="image">Qdrant image reference recorded after replica verification.</param>
    /// <param name="topology">The one-, two- or three-node native topology to configure and verify.</param>
    /// <param name="nodeClients">Optional clients for each Qdrant node used by replica verification; the target disposes distinct clients.</param>
    public QdrantTarget(HttpClient http, string runId, string image,
        ComparisonTopology topology = ComparisonTopology.Standalone, HttpClient[]? nodeClients = null)
    {
        client = http;
        this.image = image;
        this.topology = topology;
        seedSuffix = QdrantNativePolicy.SeedSuffix(topology);
        querySuffix = QdrantNativePolicy.QuerySuffix(topology);
        collection = CollectionPrefix + Guid.Parse(runId).ToString(RunIdentityFormat);
        this.nodeClients = nodeClients ?? [http];
        ownedClients = this.nodeClients.Prepend(http).Distinct<HttpClient>(ReferenceEqualityComparer.Instance).ToArray();
        if (ownedClients.Length != this.nodeClients.Length + (this.nodeClients.Contains(http) ? 0 : 1))
        {
            throw new ComparisonFailureException("QdrantDuplicateClientOwnership");
        }
    }

    /// <summary>Creates a native target with centrally validated execution limits.</summary>
    /// <param name="http">The collection HTTP client transferred to this target.</param>
    /// <param name="runId">The isolated run identity.</param>
    /// <param name="image">The pinned native image.</param>
    /// <param name="topology">The actual native member topology.</param>
    /// <param name="nodeClients">Clients for each configured native member.</param>
    /// <param name="executionOptions">The centrally registered and validated execution policy.</param>
    public QdrantTarget(HttpClient http, string runId, string image, ComparisonTopology topology,
        HttpClient[]? nodeClients, IOptions<NativeComparisonExecutionOptions> executionOptions)
        : this(http, runId, image, topology, nodeClients)
    {
        this.executionOptions = NativeComparisonExecutionOptions.Require(executionOptions);
    }

    /// <summary>Gets the server version, native topology, write acknowledgement, exact-read, transport, and replica evidence.</summary>
    public TargetProfile Profile { get; private set; } = new("Qdrant", "unverified", "unverified native topology",
        "wait=true seed upserts; pre-timing copy verification", "exact=true query; seeded collection static during queries", "HTTP JSON", "Aspire API key; no row/field policy", null);
    /// <summary>Reports support only for exact vector search.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> only when <paramref name="scenario"/> is vector search.</returns>
    public bool Supports(Scenario scenario) => scenario == Scenario.VectorExact;

    /// <summary>Creates the collection, seeds vectors with acknowledged writes, and verifies point copies across configured nodes.</summary>
    /// <param name="corpus">The deterministic vector corpus and vector dimensions and result limit.</param>
    /// <param name="cancellationToken">A token that cancels collection setup, seeding, and replica verification.</param>
    /// <returns>A task that completes after the verified profile has been recorded.</returns>
    public async Task InitializeAsync(IComparisonCorpus corpus, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (corpus is not BenchmarkDataset dataset)
        {
            throw new NotSupportedException("This target does not support the bounded scaled document corpus.");
        }
        topK = dataset.Options.TopK;
        QdrantReplicaProof.ValidateClients(nodeClients, topology);
        collectionCreationAttempted = true;
        using (var create = await client.PutAsJsonAsync($"/collections/{collection}",
            QdrantNativePolicy.CreateCollection(dataset.Options.Dimensions, topology), cancellationToken))
        {
            create.EnsureSuccessStatusCode();
        }

        await SeedAsync(dataset, cancellationToken);
        var proof = await QdrantReplicaProof.VerifyAsync(nodeClients, collection, dataset.Documents.Length, topology, cancellationToken);
        Profile = Profile with
        {
            Version = proof.Version,
            Topology = QdrantNativePolicy.TopologyLabel(topology),
            WriteAcknowledgement = QdrantNativePolicy.WriteContract(topology),
            ReadContract = QdrantNativePolicy.ReadContract(topology),
            Image = image,
            Cluster = proof.Evidence
        };
    }

    private async Task SeedAsync(BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        foreach (var batch in dataset.Documents.Chunk(64))
        {
            var points = batch.Select(document => new
            {
                id = document.Number + 1,
                vector = document.Vector,
                payload = new { id = document.Id, document = JsonSerializer.Deserialize<JsonElement>(document.Json) }
            });
            using var response = await client.PutAsJsonAsync($"/collections/{collection}/points?wait=true{seedSuffix}",
                new { points }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    /// <summary>Opens an exact-vector query session over the seeded collection.</summary>
    /// <param name="cancellationToken">A token accepted for the common target contract; session construction does not perform I/O.</param>
    /// <returns>A comparison session bound to this target’s collection.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => Task.FromResult<IComparisonSession>(new Session(this));

    /// <summary>Deletes the run-specific collection and disposes all distinct HTTP clients owned by this target.</summary>
    /// <returns>A value task that completes after collection cleanup and client disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupTimeoutSeconds));
        try
        {
            if (vectorOperations is not null)
            {
                await vectorOperations.DisposeAsync();
            }
            if (collectionCreationAttempted)
            {
                using var response = await client.DeleteAsync(new Uri($"/collections/{collection}", UriKind.RelativeOrAbsolute), timeout.Token);
            }
        }
        finally
        {
            foreach (var owned in ownedClients)
            {
                owned.Dispose();
            }
        }
    }

    /// <summary>Executes exact vector queries for the Qdrant target.</summary>
    private sealed class Session(QdrantTarget target) : IComparisonSession
    {
        public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            if (scenario != Scenario.VectorExact)
            {
                throw new NotSupportedException();
            }

            using var response = await target.client.PostAsJsonAsync($"/collections/{target.collection}/points/query{target.querySuffix}",
                new { query = document.Vector, limit = target.topK, @params = new { exact = true }, with_payload = true, with_vector = false }, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var result = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            return new(Neighbors: ImmutableCollectionsMarshal.AsImmutableArray(result!.RootElement.GetProperty(ResultProperty).GetProperty(PointsProperty).EnumerateArray().Select(point =>
            {
                var payload = point.GetProperty(PayloadProperty);
                return new FoundDocument(payload.GetProperty(IdProperty).GetString()!, payload.GetProperty(DocumentProperty).GetRawText());
            }).ToArray()));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
