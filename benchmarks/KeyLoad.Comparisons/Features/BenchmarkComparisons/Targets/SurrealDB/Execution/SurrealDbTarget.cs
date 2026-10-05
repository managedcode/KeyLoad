using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Compares SurrealDB native document, exact vector and directed relation operations.</summary>
/// <param name = "http">Owned authenticated native HTTP client.</param>
/// <param name = "runId">Unique corpus scope.</param>
/// <param name = "executionOptions">The required validated operational limits.</param>
/// <param name = "image">Immutable native server image.</param>
public sealed class SurrealDbTarget(HttpClient http, string runId, string image, IOptions<NativeComparisonExecutionOptions> executionOptions) : IComparisonTarget
{
    private const string OnePersistentRocksDBNativeNode = "one persistent RocksDB native node";
    private const string SingleNativeCommittedWriteNotFaultQualified = "single native committed write; not fault-qualified";
    private const string CommittedPrimaryNativeDirectedGraphAndExactCosine = "committed primary; native directed graph and exact cosine KNN";
    private const string POSTSqlJSON = "POST /sql JSON";
    private const string RootBasicAuthentication = "root Basic authentication";
    private const int SingleResultCardinality = 1;
    private const string SingleCommunityNode = "single Community node";
    private const string PersistentRocksDBDirectory = "persistent RocksDB directory";
    private const string NativeDEFINETABLESCHEMALESSDEFINETABLESCHEMALESSFormatTemplate = "DEFINE TABLE {0} SCHEMALESS; DEFINE TABLE {1} SCHEMALESS;";
    private static readonly System.Text.CompositeFormat NativeDEFINETABLESCHEMALESSDEFINETABLESCHEMALESSFormat = System.Text.CompositeFormat.Parse(NativeDEFINETABLESCHEMALESSDEFINETABLESCHEMALESSFormatTemplate);
    private const char SqlStatementSeparator = ' ';
    private const string NativeRELATEFormatTemplate = "RELATE {0}:{1}->{2}:{3}->{4}:{5};";
    private static readonly System.Text.CompositeFormat NativeRELATEFormat = System.Text.CompositeFormat.Parse(NativeRELATEFormatTemplate);
    private const string NativeREMOVETABLEREMOVETABLEFormatTemplate = "REMOVE TABLE {0}; REMOVE TABLE {1};";
    private static readonly System.Text.CompositeFormat NativeREMOVETABLEREMOVETABLEFormat = System.Text.CompositeFormat.Parse(NativeREMOVETABLEREMOVETABLEFormatTemplate);
    private readonly IOptions<NativeComparisonExecutionOptions> execution = NativeComparisonExecutionOptions.Require(executionOptions);
    private NativeComparisonExecutionOptions Policy => execution.Value;
    private const string RunIdentityFormat = "N";
    private readonly string table = SurrealDbNativeTokens.TokenKeyloadDoc + Guid.Parse(runId).ToString(RunIdentityFormat);
    private readonly string edge = SurrealDbNativeTokens.TokenKeyloadLink + Guid.Parse(runId).ToString(RunIdentityFormat);
    private int count;
    private int depth;
    private int topK;
    private bool ownsData;
    /// <inheritdoc/>
    public TargetProfile Profile { get; private set; } = new(SurrealDbNativeTokens.TokenSurrealDB, SurrealDbNativeTokens.TokenUnverified, OnePersistentRocksDBNativeNode, SingleNativeCommittedWriteNotFaultQualified, CommittedPrimaryNativeDirectedGraphAndExactCosine, POSTSqlJSON, RootBasicAuthentication, image)
    {
        Cluster = new(SingleResultCardinality, SingleResultCardinality, SingleCommunityNode, [PersistentRocksDBDirectory])
    };

    /// <inheritdoc/>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete or Scenario.GraphNeighbors or Scenario.GraphTraverse or Scenario.VectorExact;
    /// <inheritdoc/>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        Profile = Profile with
        {
            Version = await SurrealDbServer.VerifyAsync(http, Policy, cancellationToken).ConfigureAwait(false)
        };
        count = dataset.Documents.Count;
        depth = dataset.Settings.GraphDepth;
        topK = dataset.Settings.TopK;
        ownsData = true;
        await SurrealDbSqlTransport.ExecuteAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeDEFINETABLESCHEMALESSDEFINETABLESCHEMALESSFormat, table, edge), Policy, cancellationToken).ConfigureAwait(false);
        await SurrealDbReadbackIndex.CreateAsync(http, table, Policy, cancellationToken).ConfigureAwait(false);
        foreach (var batch in dataset.Documents.Chunk(Policy.WriteBatchCapacity))
        {
            var sql = string.Join(SqlStatementSeparator, batch.Select(document => SurrealDbDocumentSql.Create(table, document)));
            await SurrealDbSqlTransport.ExecuteAsync(http, sql, Policy, cancellationToken).ConfigureAwait(false);
        }

        foreach (var batch in dataset.Edges.Chunk(Policy.WriteBatchCapacity))
        {
            var sql = string.Join(SqlStatementSeparator, batch.Select(link => string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeRELATEFormat, table, SurrealDbDocumentSql.Key(link.From), edge, SurrealDbDocumentSql.Key(link.Id), table, SurrealDbDocumentSql.Key(link.To))));
            await SurrealDbSqlTransport.ExecuteAsync(http, sql, Policy, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IComparisonSession>(new SurrealDbSession(http, table, edge, count, depth, topK, execution));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ownsData)
            {
                using var timeout = new CancellationTokenSource(Policy.CleanupTimeout);
                await SurrealDbSqlTransport.ExecuteAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeREMOVETABLEREMOVETABLEFormat, edge, table), Policy, timeout.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            http.Dispose();
        }
    }
}
