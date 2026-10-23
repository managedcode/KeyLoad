using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Compares HelixDB execution document and directed graph operations on one persisted writer.</summary>
/// <param name = "http">Owned execution HTTP client.</param>
/// <param name = "runId">Unique execution label scope.</param>
/// <param name = "executionOptions">The required validated operational limits.</param>
/// <param name="provider">Borrowed clock; defaults to the system provider.</param>
/// <param name = "image">Immutable execution server image.</param>
public sealed partial class HelixDbTarget(HttpClient http, string runId, string image, IOptions<ComparisonExecutionOptions> executionOptions, TimeProvider? provider = null) : IDocumentComparisonTarget
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const string RuntimeV0PinnedServerImage = "v0.0.10; pinned execution server image";
    private const string OnePersistentLocalWriter = "one persistent execution local writer";
    private const string XHelixAwaitDurableTrueDiskFlush = "X-Helix-Await-Durable true; execution disk flush; not fault-qualified";
    private const string CommittedReadsAndDirectedGraphTraversal = "committed execution reads and directed graph traversal";
    private const string POSTV2QueryOperationTree = "POST /query operation tree";
    private const string IsolatedLocalServerNoAuthentication = "isolated local server; no authentication";
    private const string SingleLocalWriterNoReplicationConfigured = "single local writer; no replication configured";
    private const string HELIXDATADIRPersistentDirectory = "HELIX_DATA_DIR persistent directory";
    private readonly IOptions<ComparisonExecutionOptions> execution = ComparisonExecutionOptions.Require(executionOptions);
    private ComparisonExecutionOptions Policy => execution.Value;
    private readonly string label = HelixDbTokens.TokenKeyLoadDoc + Guid.Parse(runId).ToString(HelixDbTokens.TokenN);
    private int count;
    private int depth;
    private bool ownsData;
    private bool ownsEqualityIndex;
    private bool ownsRangeIndex;
    /// <inheritdoc/>
    public TargetProfile Profile
    {
        get;
    } = new(HelixDbTokens.TokenHelixDB, RuntimeV0PinnedServerImage, OnePersistentLocalWriter, XHelixAwaitDurableTrueDiskFlush, CommittedReadsAndDirectedGraphTraversal, POSTV2QueryOperationTree, IsolatedLocalServerNoAuthentication, image)
    {
        Cluster = new(1, 1, SingleLocalWriterNoReplicationConfigured, [HELIXDATADIRPersistentDirectory])
    };

    /// <inheritdoc/>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete or Scenario.GraphNeighbors or Scenario.GraphTraverse;
    /// <inheritdoc/>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        using var health = await http.GetAsync(new Uri(HelixDbTokens.TokenReadyz, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        health.EnsureSuccessStatusCode();
        count = dataset.Documents.Count;
        depth = dataset.Settings.GraphDepth;
        ownsData = true;
        var documentTiming = (dataset as DocumentComparisonCorpus)?.InitializationTiming;
        using (documentTiming?.MeasureIndex())
        {
            await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.Index(label, HelixDbTokens.TokenId, range: false), Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            ownsEqualityIndex = true;
            await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.Index(label, HelixDbTokens.TokenNumber, range: true), Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            ownsRangeIndex = true;
        }
        using (documentTiming?.MeasureLoad())
        {
            await HelixDbDocumentStorage.SeedAsync(http, label, dataset, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IComparisonSession>(new HelixDbSession(http, label, count, depth, execution, provider: timeProvider));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ownsData)
            {
                using var timeout = new CancellationTokenSource(Policy.CleanupTimeout, timeProvider);
                if (ownsEqualityIndex)
                {
                    await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.DropIndex(label, HelixDbTokens.TokenId, range: false), Policy, token: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
                }
                if (ownsSessionNumberEqualityIndex)
                {
                    await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.DropIndex(label, HelixDbTokens.TokenNumber, range: false),
                        Policy, token: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
                }
                if (ownsRangeIndex)
                {
                    await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbDocumentAst.DropIndex(label, HelixDbTokens.TokenNumber, range: true), Policy, token: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
                }
                using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbProtocol.Node(HelixDbTokens.TokenDrop, new()
                {
                    [HelixDbTokens.TokenInput] = HelixDbProtocol.Nodes(label)
                }), true), true, Policy, token: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
            }
        }
        finally
        {
            http.Dispose();
        }
    }
}
