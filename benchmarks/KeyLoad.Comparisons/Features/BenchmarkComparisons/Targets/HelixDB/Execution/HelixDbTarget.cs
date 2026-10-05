using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Compares HelixDB native document and directed graph operations on one persisted writer.</summary>
/// <param name = "http">Owned native HTTP client.</param>
/// <param name = "runId">Unique native label scope.</param>
/// <param name = "executionOptions">The required validated operational limits.</param>
/// <param name = "image">Immutable native server image.</param>
public sealed class HelixDbTarget(HttpClient http, string runId, string image, IOptions<NativeComparisonExecutionOptions> executionOptions) : IComparisonTarget
{
    private const string V0PinnedNativeServerImage = "v0.0.10; pinned native server image";
    private const string OnePersistentNativeLocalWriter = "one persistent native local writer";
    private const string XHelixAwaitDurableTrueNativeDiskFlush = "X-Helix-Await-Durable true; native disk flush; not fault-qualified";
    private const string CommittedNativeReadsAndDirectedGraphTraversal = "committed native reads and directed graph traversal";
    private const string POSTV2QueryOperationTree = "POST /v2/query operation tree";
    private const string IsolatedLocalServerNoAuthentication = "isolated local server; no authentication";
    private const int SingleResultCardinality = 1;
    private const string SingleLocalWriterNoReplicationConfigured = "single local writer; no replication configured";
    private const string HELIXDATADIRPersistentDirectory = "HELIX_DATA_DIR persistent directory";
    private readonly NativeComparisonExecutionOptions policy = executionOptions.Value.Validate();
    private readonly string label = HelixDbNativeTokens.TokenKeyLoadDoc + Guid.Parse(runId).ToString(HelixDbNativeTokens.TokenN);
    private int count;
    private int depth;
    private bool ownsData;
    /// <inheritdoc/>
    public TargetProfile Profile { get; } = new(HelixDbNativeTokens.TokenHelixDB, V0PinnedNativeServerImage, OnePersistentNativeLocalWriter, XHelixAwaitDurableTrueNativeDiskFlush, CommittedNativeReadsAndDirectedGraphTraversal, POSTV2QueryOperationTree, IsolatedLocalServerNoAuthentication, image)
    {
        Cluster = new(SingleResultCardinality, SingleResultCardinality, SingleLocalWriterNoReplicationConfigured, [HELIXDATADIRPersistentDirectory])
    };

    /// <inheritdoc/>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete or Scenario.GraphNeighbors or Scenario.GraphTraverse;
    /// <inheritdoc/>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        using var health = await http.GetAsync(new Uri(HelixDbNativeTokens.TokenReadyz, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        health.EnsureSuccessStatusCode();
        count = dataset.Documents.Count;
        depth = dataset.Settings.GraphDepth;
        ownsData = true;
        await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.Index(label, HelixDbNativeTokens.TokenId, range: false), policy, cancellationToken).ConfigureAwait(false);
        await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.Index(label, HelixDbNativeTokens.TokenNumber, range: true), policy, cancellationToken).ConfigureAwait(false);
        await HelixDbDocumentStorage.SeedAsync(http, label, dataset, policy, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IComparisonSession>(new HelixDbSession(http, label, count, depth, policy));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ownsData)
            {
                using var timeout = new CancellationTokenSource(policy.CleanupTimeout);
                using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbProtocol.Node(HelixDbNativeTokens.TokenDrop, new() { [HelixDbNativeTokens.TokenInput] = HelixDbProtocol.Nodes(label) }), true), true, policy, timeout.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            http.Dispose();
        }
    }
}
