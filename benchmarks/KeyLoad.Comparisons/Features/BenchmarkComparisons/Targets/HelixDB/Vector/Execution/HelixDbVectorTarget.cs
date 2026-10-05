using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Runs the actual HelixDB opaque ANN API on one persistent local server.</summary>
/// <param name = "http">The HTTP client transferred to this target.</param>
/// <param name = "executionOptions">The required validated operational limits.</param>
/// <param name = "image">The immutable server image.</param>
/// <param name = "runId">The unique isolated corpus scope.</param>
public sealed class HelixDbVectorTarget(HttpClient http, string image, string runId, IOptions<NativeComparisonExecutionOptions> executionOptions) : IVectorComparisonTarget
{
    private const string V0PinnedServerImage = "v0.0.10; pinned server image";
    private const string OnePersistentLocalServerHELIXDATADIR = "one persistent local server; HELIX_DATA_DIR";
    private const string XHelixAwaitDurableTrueNativeDiskFlush = "X-Helix-Await-Durable true; native disk flush; not fault-qualified";
    private const string NativeCommittedReadsStrongSearchConsistency = "native committed reads; strong search consistency";
    private const string POSTV2QueryJSONOperationTree = "POST /v2/query JSON operation tree";
    private const string IsolatedLocalServerNoAuthentication = "isolated local server; no authentication";
    private const int SingleResultCardinality = 1;
    private const string SingleNativeLocalWriterNoReplicationConfigured = "single native local writer; no replication configured";
    private const string PersistentNativeDirectory = "persistent native directory";
    private const string StrongSearchConsistency = "strong search consistency";
    private const string HelixDBExposesNativeANNNotNamedExactHNSW = "HelixDB exposes native ANN, not named exact/HNSW/IVFFlat tuning.";
    private const string NoNativeEXPLAINEndpointExecutedAST = "No native EXPLAIN endpoint; executed AST=";
    private const int CanonicalTopK = 10;
    private const string ObservedIndexLifecycle = "; observed index lifecycle=";
    private const int EmptyResultCount = 0;
    private readonly IOptions<NativeComparisonExecutionOptions> execution = NativeComparisonExecutionOptions.Require(executionOptions);
    private NativeComparisonExecutionOptions Policy => execution.Value;
    private const string RunIdentityFormat = "N";
    private readonly string label = HelixDbNativeTokens.TokenKeyLoadVector + Guid.Parse(runId).ToString(RunIdentityFormat);
    private int corpusCount;
    private int dimensions;
    private bool ownsData;
    private bool ownsIndex;
    private string? indexDefinition;
    /// <inheritdoc/>
    public string Name => HelixDbNativeTokens.TokenHelixDB;
    /// <inheritdoc/>
    public TargetProfile Profile { get; private set; } = new(HelixDbNativeTokens.TokenHelixDB, V0PinnedServerImage, OnePersistentLocalServerHELIXDATADIR, XHelixAwaitDurableTrueNativeDiskFlush, NativeCommittedReadsStrongSearchConsistency, POSTV2QueryJSONOperationTree, IsolatedLocalServerNoAuthentication, image)
    {
        Cluster = new(SingleResultCardinality, SingleResultCardinality, SingleNativeLocalWriterNoReplicationConfigured, [PersistentNativeDirectory, StrongSearchConsistency])
    };

    /// <inheritdoc/>
    public bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode) => indexKind == VectorIndexKind.NativeAnn && Enum.IsDefined(queryMode);
    /// <inheritdoc/>
    public async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        using var health = await http.GetAsync(new Uri(HelixDbNativeTokens.TokenReadyz, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        health.EnsureSuccessStatusCode();
        ownsData = true;
        corpusCount = await HelixDbVectorStorage.IngestAsync(http, label, documents, Policy, cancellationToken).ConfigureAwait(false);
        return corpusCount;
    }

    /// <inheritdoc/>
    public async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Supports(profile.IndexKind, profile.QueryMode))
        {
            throw new NotSupportedException(HelixDBExposesNativeANNNotNamedExactHNSW);
        }

        dimensions = profile.Dimensions;
        ownsIndex = true;
        var receipt = await HelixDbIndexLifecycle.BuildAsync(http, label, dimensions, Policy, cancellationToken).ConfigureAwait(false);
        indexDefinition = receipt.Definition;
        return receipt;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken) => HelixDbVectorStorage.ReadbackAsync(http, label, corpusCount, Policy, cancellationToken);
    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK, VectorQueryMode mode, CancellationToken cancellationToken)
    {
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbVectorAst.Search(label, query.Span, topK, mode), false), false, Policy, cancellationToken).ConfigureAwait(false);
        return HelixDbProtocol.Rows(response).EnumerateArray().Select(ReadNeighbor).ToArray();
    }

    /// <inheritdoc/>
    public Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The public HelixDB API has no EXPLAIN operation: retain the actual executed AST and observed lifecycle receipt.
        return Task.FromResult(NoNativeEXPLAINEndpointExecutedAST + HelixDbVectorAst.Search(label, query.Span, CanonicalTopK, mode).ToJsonString() + ObservedIndexLifecycle + (indexDefinition ?? throw new InvalidOperationException(HelixDbNativeTokens.TokenHelixDbIndexNotReady)));
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbVectorAst.Update(label, update), true), true, Policy, cancellationToken).ConfigureAwait(false);
        if (HelixDbProtocol.Rows(response).GetArrayLength() != SingleResultCardinality)
        {
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbUpdateCardinalityMismatch);
        }
    }

    /// <inheritdoc/>
    public async Task<VectorReadback?> ReadAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbVectorAst.Read(label, id), false), false, Policy, cancellationToken).ConfigureAwait(false);
        var rows = HelixDbProtocol.Rows(response);
        return rows.GetArrayLength() == EmptyResultCount ? null : HelixDbVectorStorage.Read(rows[EmptyResultCount]);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(Policy.CleanupTimeout);
            if (ownsIndex)
            {
                await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbVectorAst.Index(label, dimensions, drop: true), Policy, timeout.Token).ConfigureAwait(false);
            }

            if (ownsData)
            {
                using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbProtocol.Node(HelixDbNativeTokens.TokenDrop, new() { [HelixDbNativeTokens.TokenInput] = HelixDbProtocol.Nodes(label) }), true), true, Policy, timeout.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            http.Dispose();
        }
    }

    private static VectorNeighbor ReadNeighbor(JsonElement row)
    {
        var distance = row.GetProperty(HelixDbNativeTokens.MetaTokenDistance).GetDouble();
        if (!double.IsFinite(distance))
        {
            throw new ComparisonFailureException(HelixDbNativeTokens.TokenHelixDbInvalidDistance);
        }

        return new(row.GetProperty(HelixDbNativeTokens.TokenId).GetString()!, distance);
    }
}
