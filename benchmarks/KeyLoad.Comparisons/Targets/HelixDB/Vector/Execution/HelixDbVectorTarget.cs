using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Runs the actual HelixDB opaque ANN API on one persistent local server.</summary>
/// <param name = "http">The HTTP client transferred to this target.</param>
/// <param name = "executionOptions">The required validated operational limits.</param>
/// <param name="provider">Borrowed clock; defaults to the system provider.</param>
/// <param name = "image">The immutable server image.</param>
/// <param name = "runId">The unique isolated corpus scope.</param>
public sealed class HelixDbVectorTarget(HttpClient http, string image, string runId, IOptions<ComparisonExecutionOptions> executionOptions, TimeProvider? provider = null) : IVectorComparisonTarget
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const string V0PinnedServerImage = "v0.0.10; pinned server image";
    private const string OnePersistentLocalServerHELIXDATADIR = "one persistent local server; HELIX_DATA_DIR";
    private const string XHelixAwaitDurableTrueDiskFlush = "X-Helix-Await-Durable true; execution disk flush; not fault-qualified";
    private const string CommittedReadsStrongSearchConsistency = "execution committed reads; strong search consistency";
    private const string POSTV2QueryJSONOperationTree = "POST /query JSON operation tree";
    private const string IsolatedLocalServerNoAuthentication = "isolated local server; no authentication";
    private const string RuntimeSingleLocalWriterNoReplicationConfigured = "single execution local writer; no replication configured";
    private const string PersistentDirectory = "persistent execution directory";
    private const string StrongSearchConsistency = "strong search consistency";
    private const string HelixDBExposesANNNotNamedExactHNSW = "HelixDB exposes execution ANN, not named exact/HNSW/IVFFlat tuning.";
    private const string NoEXPLAINEndpointExecutedAST = "No execution EXPLAIN endpoint; executed AST=";
    private const int CanonicalTopK = 10;
    private const string ObservedIndexLifecycle = "; observed index lifecycle=";
    private readonly IOptions<ComparisonExecutionOptions> execution = ComparisonExecutionOptions.Require(executionOptions);
    private ComparisonExecutionOptions Policy => execution.Value;
    private const string RunIdentityFormat = "N";
    private readonly string label = HelixDbTokens.TokenKeyLoadVector + Guid.Parse(runId).ToString(RunIdentityFormat);
    private int corpusCount;
    private int dimensions;
    private bool ownsData;
    private readonly Dictionary<string, string> scalarIndexes = new(StringComparer.Ordinal);
    private bool ownsIndex;
    private string? indexDefinition;
    /// <inheritdoc/>
    public string Name => HelixDbTokens.TokenHelixDB;
    /// <inheritdoc/>
    public TargetProfile Profile
    {
        get; private set;
    } = new(HelixDbTokens.TokenHelixDB, V0PinnedServerImage, OnePersistentLocalServerHELIXDATADIR, XHelixAwaitDurableTrueDiskFlush, CommittedReadsStrongSearchConsistency, POSTV2QueryJSONOperationTree, IsolatedLocalServerNoAuthentication, image)
    {
        Cluster = new(1, 1, RuntimeSingleLocalWriterNoReplicationConfigured, [PersistentDirectory, StrongSearchConsistency])
    };

    /// <inheritdoc/>
    public bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode) => indexKind == VectorIndexKind.RuntimeAnn && Enum.IsDefined(queryMode);
    /// <inheritdoc/>
    public async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        using var health = await http.GetAsync(new Uri(HelixDbTokens.TokenReadyz, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        health.EnsureSuccessStatusCode();
        ownsData = true;
        await HelixDbVectorSetup.CreateAsync(http, label, scalarIndexes, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        corpusCount = await HelixDbVectorStorage.IngestAsync(http, label, documents, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        return corpusCount;
    }

    /// <inheritdoc/>
    public async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Supports(profile.IndexKind, profile.QueryMode))
        {
            throw new NotSupportedException(HelixDBExposesANNNotNamedExactHNSW);
        }

        dimensions = profile.Dimensions;
        ownsIndex = true;
        var receipt = await HelixDbIndexLifecycle.BuildAsync(http, label, dimensions, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        indexDefinition = receipt.Definition;
        var parameters = new Dictionary<string, string>(receipt.Parameters, StringComparer.Ordinal);
        foreach (var scalarIndex in scalarIndexes)
        {
            parameters[scalarIndex.Key] = scalarIndex.Value;
        }
        return receipt with
        {
            Parameters = parameters
        };
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken) => HelixDbVectorStorage.ReadbackAsync(http, label, corpusCount, Policy, timeProvider, cancellationToken);
    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK, VectorQueryMode mode, CancellationToken cancellationToken)
    {
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbVectorAst.Search(label, query.Span, topK, mode), false), false, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        return HelixDbProtocol.Rows(response).EnumerateArray().Select(ReadNeighbor).ToArray();
    }

    /// <inheritdoc/>
    public Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // The public HelixDB API has no EXPLAIN operation: retain the actual executed AST and observed lifecycle receipt.
        return Task.FromResult(NoEXPLAINEndpointExecutedAST + HelixDbVectorAst.Search(label, query.Span, CanonicalTopK, mode).ToJsonString() + ObservedIndexLifecycle + (indexDefinition ?? throw new InvalidOperationException(HelixDbTokens.TokenHelixDbIndexNotReady)));
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbVectorAst.Update(label, update), true), true, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        if (HelixDbProtocol.Rows(response).GetArrayLength() != 1)
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbUpdateCardinalityMismatch);
        }
    }

    /// <inheritdoc/>
    public async Task<VectorReadback?> ReadAsync(string id, CancellationToken cancellationToken)
    {
        using var response = await HelixDbProtocol.QueryAsync(http, HelixDbProtocol.Batch(HelixDbVectorAst.Read(label, id), false), false, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var rows = HelixDbProtocol.Rows(response);
        if (rows.GetArrayLength() > 1)
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbInvalidRows);
        }
        return rows.GetArrayLength() == 0 ? null : HelixDbVectorStorage.Read(rows[0]);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            using var timeout = new CancellationTokenSource(Policy.CleanupTimeout, timeProvider);
            if (ownsIndex)
            {
                await HelixDbIndexLifecycle.ExecuteAsync(http, HelixDbVectorAst.Index(label, dimensions, drop: true), Policy, token: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
            }

            if (ownsData)
            {
                await HelixDbVectorSetup.DropAsync(http, label, scalarIndexes.Keys, Policy, token: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
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

    private VectorNeighbor ReadNeighbor(JsonElement row)
    {
        var distance = row.GetProperty(HelixDbTokens.MetaTokenDistance).GetDouble();
        if (!double.IsFinite(distance))
        {
            throw new ComparisonFailureException(HelixDbTokens.TokenHelixDbInvalidDistance);
        }

        return new(row.GetProperty(HelixDbTokens.TokenId).GetString()!, distance);
    }
}
