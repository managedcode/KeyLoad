using System.Collections.Immutable;
using KeyLoad.Client;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares the database through its authenticated SDK and verifies actual acknowledged data copies.</summary>
/// <param name="http">The primary HTTP client used by the KeyLoad SDK client; target disposal disposes it.</param>
/// <param name="apiKey">The credential passed to the KeyLoad SDK client for authenticated operations.</param>
/// <param name="runId">Run identifier used to derive the isolated benchmark partition.</param>
/// <param name="lifecycleOptions">Centrally validated native lifecycle policy.</param>
/// <param name="nativeExecutionOptions">Centrally validated native adapter execution policy.</param>
/// <param name="diagnosticOptions">Centrally validated privacy-preserving diagnostic bounds.</param>
/// <param name="admissionOptions">Centrally validated isolated node and observer admission profile.</param>
/// <param name="clientOptions">Centrally validated SDK transport budget.</param>
/// <param name="translationOptions">Centrally validated SDK expression budgets.</param>
/// <param name="image">Optional database image reference included in the initial target profile.</param>
/// <param name="peers">Optional peer HTTP clients used to observe replica copies; the target disposes distinct clients.</param>
/// <param name="expectedNodes">Expected native silos and persisted copies for the selected comparison cell.</param>
/// <param name="provider">Borrowed clock; defaults to the system provider.</param>
public sealed partial class KeyLoadTarget(HttpClient http, string apiKey, string runId,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions,
    IOptions<NativeComparisonDiagnosticOptions> diagnosticOptions,
    IOptions<IsolatedKeyLoadAdmissionOptions> admissionOptions, IOptions<KeyLoadClientExecutionOptions> clientOptions,
    IOptions<QueryTranslationOptions> translationOptions, string? image = null,
    HttpClient[]? peers = null, int expectedNodes = KeyLoadTarget.ExpectedNodesDefault, TimeProvider? provider = null) : IDocumentComparisonTarget
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const int ExpectedNodesDefault = 3;
    private const string BenchmarkTenantPrefix = "benchmark-";
    private const string ComparisonDatabaseName = "comparison";
    private const string WorkloadPartitionName = "workload";
    private const string InitializeAsyncStatusText = "Status";
    private const string ConfigureCorpusAuthenticatedPrimaryDocumentReadsS1SeedsDocumentsOnlyWithNoVectorText = "authenticated primary document reads; S1 seeds documents only, with no vector, queue, graph or event records";
    private const string SeededVectorModel = "seeded-float32";
    private const string VectorModelVersion = "1";
    private const int EmptyCount = 0;
    private const string DocumentsCollectionName = "documents";
    private const string JobsQueueName = "jobs";
    private const string LinksGraphName = "links";
    private const string ConfigureResourcesAsyncConfigureText = "Configure:";
    private const string EmbeddingJsonPointer = "/embedding";
    private const string SeedDocumentsAsyncSeedDocumentsText = "SeedDocuments";
    private const string SeedGraphAsyncSeedGraphText = "SeedGraph";

    private readonly NativeComparisonExecutionOptions execution = NativeComparisonExecutionOptions.Require(nativeExecutionOptions).Value;

    private readonly IOptions<HttpAdmissionLimits> httpAdmissionOptions = IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(admissionOptions);
    private const string TransactionDomainId = "shared";
    private readonly ComparisonLifecycleOptions lifecycle = lifecycleOptions.Value;
    private readonly int expectedNodeCount = KeyLoadTopologyProfile.ValidateExpectedNodes(expectedNodes);
    private readonly KeyLoadClient client = new(http, apiKey, clientOptions);
    private readonly HttpClient[] peerClients = peers is null
        ? [http]
        : peers.Contains(http, ReferenceEqualityComparer.Instance) ? peers : [.. peers, http];
    private readonly string credential = apiKey;
    private readonly PartitionRef partition = new(BenchmarkTenantPrefix + runId, ComparisonDatabaseName, WorkloadPartitionName, TransactionDomainId);
    private VectorSpace space = null!;
    private int topK;
    private int graphDepth, graphVertices, graphEdges, expectedCorpusCount;
    private string[] admissionObservations = [];
    internal bool RequireIsolatedAdmission
    {
        get; init;
    }
    /// <summary>Gets the declared copy acknowledgement, durability, API and authorization contract before runtime observation.</summary>
    public TargetProfile Profile { get; private set; } = KeyLoadTopologyProfile.CreateProfile(expectedNodes, image);
    /// <summary>Reports support for document, vector, queue, graph, and event-stream comparison scenarios.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for a scenario implemented by this target; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.VectorExact
        or Scenario.QueueCycle or Scenario.GraphNeighbors or Scenario.GraphTraverse or Scenario.StreamAppend or Scenario.StreamRead
        or Scenario.DocumentUpdate or Scenario.DocumentDelete;
    /// <summary>Requires the selected native topology, checks persisted copy receipts and reads through every endpoint.</summary>
    /// <param name="dataset">The deterministic documents, vectors, graph edges, and workload options to provision.</param>
    /// <param name="cancellationToken">A token that cancels status checks and database operations.</param>
    /// <returns>A task that completes after seeding and replica observation.</returns>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        admissionObservations = await KeyLoadAdmissionObserver.ObserveAsync(RequireIsolatedAdmission,
            peerClients, credential, clientOptions, httpAdmissionOptions, cancellationToken).ConfigureAwait(false);
        var status = KeyLoadClientResults.Success(await client.StatusAsync(cancellationToken), InitializeAsyncStatusText);
        if (status.SiloCount != expectedNodeCount || status.ReplicaCount != expectedNodeCount ||
            status.Durability != DurabilityProfile.ProcessDurable)
        {
            throw new ComparisonFailureException(expectedNodeCount == ExpectedNodesDefault ? ClusterRequired : BenchmarkTopologyMismatch);
        }

        var scaled = dataset.Settings is ScaledComparisonProfile || dataset is DocumentComparisonCorpus;
        ConfigureCorpus(dataset, scaled);
        await ConfigureResourcesAsync(scaled, cancellationToken);
        var documentTiming = (dataset as DocumentComparisonCorpus)?.InitializationTiming;
        documentTiming?.IndexNotApplicable();
        using (documentTiming?.MeasureLoad())
        {
            await SeedDocumentsAsync(dataset, scaled, cancellationToken);
        }

        await SeedGraphAsync(dataset, scaled, cancellationToken);
        if (!scaled && dataset is BenchmarkDataset control)
        {
            await KeyLoadEventOperations.SeedAsync(client, partition, control, expectedNodeCount, cancellationToken);
        }
        await ObserveCopiesAsync(cancellationToken);
    }

    private void ConfigureCorpus(IComparisonCorpus dataset, bool scaled)
    {
        if (scaled)
        {
            Profile = Profile with
            {
                ReadContract = ConfigureCorpusAuthenticatedPrimaryDocumentReadsS1SeedsDocumentsOnlyWithNoVectorText
            };
        }
        expectedCorpusCount = dataset.Documents.Count;
        space = scaled ? null! : new(ComparisonDatabaseName, dataset.Settings.Dimensions, DistanceMetric.Cosine, SeededVectorModel, VectorModelVersion);
        topK = dataset.Settings.TopK;
        graphDepth = dataset.Settings.GraphDepth;
        graphVertices = dataset.GraphVertexCount;
        graphEdges = scaled ? EmptyCount : Math.Max(SingleItemCount, dataset.Edges.Count);
    }

    private async Task ConfigureResourcesAsync(bool scaled, CancellationToken cancellationToken)
    {
        var resources = scaled ? new[] { (DocumentsCollectionName, ResourceKind.Collection) }
            : new[] { (DocumentsCollectionName, ResourceKind.Collection), (JobsQueueName, ResourceKind.WorkQueue), (LinksGraphName, ResourceKind.Graph),
                (KeyLoadEventOperations.EventsName, ResourceKind.StreamSet) };
        foreach (var (name, kind) in resources)
        {
            KeyLoadClientResults.Success(await client.ConfigureResourceAsync(Guid.NewGuid(),
                new(partition.TenantId, partition.DatabaseId, new(name, kind, partition.TransactionDomainId)), cancellationToken), ConfigureResourcesAsyncConfigureText + name);
        }
    }

    private async Task SeedDocumentsAsync(IComparisonCorpus dataset, bool scaled, CancellationToken cancellationToken)
    {
        if (scaled)
        {
            await KeyLoadScaledCorpusSeeder.SeedAsync(client, partition, dataset.Documents, expectedNodeCount, nativeExecutionOptions, cancellationToken);
            return;
        }
        foreach (var document in dataset.Documents)
        {
            ImmutableArray<Mutation> mutations = [new PutDocument(DocumentsCollectionName, document.Id, document.Json, EmptyCount),
                new PutVector(DocumentsCollectionName, document.Id, EmbeddingJsonPointer, document.Vector, space, SingleItemCount)];
            var receipt = KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, mutations), cancellationToken), SeedDocumentsAsyncSeedDocumentsText);
            KeyLoadCopyAcknowledgements.Require(receipt, expectedNodeCount);
        }
    }

    private async Task SeedGraphAsync(IComparisonCorpus dataset, bool scaled, CancellationToken cancellationToken)
    {
        if (scaled)
        {
            return;
        }
        foreach (var batch in dataset.Edges.Chunk(execution.KeyLoadGraphSeedBatchSize))
        {
            var mutations = batch.Select(edge => (Mutation)new UpsertEdge(LinksGraphName, edge.Id,
                new(partition, DocumentsCollectionName, edge.From), new(partition, DocumentsCollectionName, edge.To), LinksGraphName, ExpectedRevision: EmptyCount));
            var receipt = KeyLoadClientResults.Success(await client.CommitAsync(new(Guid.NewGuid(), partition, [.. mutations]), cancellationToken), SeedGraphAsyncSeedGraphText);
            KeyLoadCopyAcknowledgements.Require(receipt, expectedNodeCount);
        }
    }
    /// <summary>Opens a session that executes supported operations through the SDK client.</summary>
    /// <param name="cancellationToken">A token accepted for the common target contract; this implementation creates the session without I/O.</param>
    /// <returns>A comparison session bound to this target and its benchmark partition.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => Task.FromResult<IComparisonSession>(new KeyLoadComparisonSession(client, partition, space, topK,
            graphDepth, graphVertices, graphEdges, expectedCorpusCount, expectedNodeCount, lifecycleOptions, nativeExecutionOptions, translationOptions, provider: timeProvider));
    /// <summary>Disposes the distinct HTTP clients owned by this target.</summary>
    /// <returns>A value task that completes after client disposal.</returns>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await CleanupDocumentNamespaceAsync().ConfigureAwait(false);
        }
        finally
        {
            foreach (var peer in peerClients.Distinct())
            {
                peer.Dispose();
            }
        }
    }

}
