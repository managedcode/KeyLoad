using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Compares SurrealDB execution document, exact vector and directed relation operations.</summary>
/// <param name = "http">Owned authenticated execution HTTP client.</param>
/// <param name = "runId">Unique corpus scope.</param>
/// <param name = "executionOptions">The required validated operational limits.</param>
/// <param name="provider">Borrowed clock; defaults to the system provider.</param>
/// <param name = "image">Immutable execution server image.</param>
public sealed partial class SurrealDbTarget(HttpClient http, string runId, string image, IOptions<ComparisonExecutionOptions> executionOptions, TimeProvider? provider = null) : IDocumentComparisonTarget
{
    private readonly TimeProvider timeProvider = provider ?? TimeProvider.System;
    private const string OnePersistentRocksDBNode = "one persistent RocksDB execution node";
    private const string SingleCommittedWriteNotFaultQualified = "single execution committed write; not fault-qualified";
    private const string CommittedPrimaryDirectedGraphAndExactCosine = "committed primary; execution directed graph and exact cosine KNN";
    private const string POSTSqlJSON = "POST /sql JSON";
    private const string RootBasicAuthentication = "root Basic authentication";
    private const string SingleCommunityNode = "single Community node";
    private const string PersistentRocksDBDirectory = "persistent RocksDB directory";
    private const string DEFINETABLESCHEMALESSDEFINETABLESCHEMALESSFormatTemplate = "DEFINE TABLE {0} SCHEMALESS; DEFINE TABLE {1} SCHEMALESS;";
    private static readonly System.Text.CompositeFormat DEFINETABLESCHEMALESSDEFINETABLESCHEMALESSFormat = System.Text.CompositeFormat.Parse(DEFINETABLESCHEMALESSDEFINETABLESCHEMALESSFormatTemplate);
    private const char SqlStatementSeparator = ' ';
    private const string RELATEFormatTemplate = "RELATE {0}:{1}->{2}:{3}->{4}:{5};";
    private static readonly System.Text.CompositeFormat RELATEFormat = System.Text.CompositeFormat.Parse(RELATEFormatTemplate);
    private const string REMOVETABLEREMOVETABLEFormatTemplate = "REMOVE TABLE {0}; REMOVE TABLE {1};";
    private static readonly System.Text.CompositeFormat REMOVETABLEREMOVETABLEFormat = System.Text.CompositeFormat.Parse(REMOVETABLEREMOVETABLEFormatTemplate);
    private readonly IOptions<ComparisonExecutionOptions> execution = ComparisonExecutionOptions.Require(executionOptions);
    private ComparisonExecutionOptions Policy => execution.Value;
    private const string RunIdentityFormat = "N";
    private readonly string table = SurrealDbTokens.TokenKeyloadDoc + Guid.Parse(runId).ToString(RunIdentityFormat);
    private readonly string edge = SurrealDbTokens.TokenKeyloadLink + Guid.Parse(runId).ToString(RunIdentityFormat);
    private int count;
    private int depth;
    private int topK;
    private bool ownsData;
    /// <inheritdoc/>
    public TargetProfile Profile
    {
        get; private set;
    } = new(SurrealDbTokens.TokenSurrealDB, SurrealDbTokens.TokenUnverified, OnePersistentRocksDBNode, SingleCommittedWriteNotFaultQualified, CommittedPrimaryDirectedGraphAndExactCosine, POSTSqlJSON, RootBasicAuthentication, image)
    {
        Cluster = new(1, 1, SingleCommunityNode, [PersistentRocksDBDirectory])
    };

    /// <inheritdoc/>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete or Scenario.GraphNeighbors or Scenario.GraphTraverse or Scenario.VectorExact;
    /// <inheritdoc/>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        Profile = Profile with
        {
            Version = await SurrealDbServer.VerifyAsync(http, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false)
        };
        count = dataset.Documents.Count;
        depth = dataset.Settings.GraphDepth;
        topK = dataset.Settings.TopK;
        ownsData = true;
        await SurrealDbSqlTransport.ExecuteAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, DEFINETABLESCHEMALESSDEFINETABLESCHEMALESSFormat, table, edge), Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        var documentTiming = (dataset as DocumentComparisonCorpus)?.InitializationTiming;
        using (documentTiming?.MeasureIndex())
        {
            await SurrealDbReadbackIndex.CreateAsync(http, table, Policy, token: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        }

        using (documentTiming?.MeasureLoad())
        {
            foreach (var batch in dataset.Documents.Chunk(Policy.WriteBatchCapacity))
            {
                var sql = string.Join(SqlStatementSeparator, batch.Select(document => SurrealDbDocumentSql.Create(table, document)));
                await SurrealDbSqlTransport.ExecuteAsync(http, sql, Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
            }

        }
        foreach (var batch in dataset.Edges.Chunk(Policy.WriteBatchCapacity))
        {
            var sql = string.Join(SqlStatementSeparator, batch.Select(link => string.Format(System.Globalization.CultureInfo.InvariantCulture, RELATEFormat, table, SurrealDbDocumentSql.Key(link.From), edge, SurrealDbDocumentSql.Key(link.Id), table, SurrealDbDocumentSql.Key(link.To))));
            await SurrealDbSqlTransport.ExecuteAsync(http, sql, Policy, cancellationToken: cancellationToken, timeProvider: timeProvider).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IComparisonSession>(new SurrealDbSession(http, table, edge, count, depth, topK, execution, provider: timeProvider));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ownsData)
            {
                using var timeout = new CancellationTokenSource(Policy.CleanupTimeout, timeProvider);
                await SurrealDbSqlTransport.ExecuteAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, REMOVETABLEREMOVETABLEFormat, edge, table), Policy, cancellationToken: timeout.Token, timeProvider: timeProvider).ConfigureAwait(false);
            }
        }
        finally
        {
            http.Dispose();
        }
    }
}
