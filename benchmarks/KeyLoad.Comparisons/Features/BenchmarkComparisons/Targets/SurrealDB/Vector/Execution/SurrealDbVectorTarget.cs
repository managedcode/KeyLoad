using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KeyLoad.Comparisons.Targets;
/// <summary>Runs SurrealDB's native brute-force and HNSW vector paths over its persistent RocksDB backend.</summary>
/// <param name = "http">Owned native HTTP client.</param>
/// <param name = "image">The immutable native server image.</param>
/// <param name = "runId">Unique corpus scope.</param>
/// <param name = "executionOptions">The required operational Policy.</param>
public sealed class SurrealDbVectorTarget(HttpClient http, string image, string runId, IOptions<NativeComparisonExecutionOptions> executionOptions) : IVectorComparisonTarget
{
    private const string SurrealDBCommunityOnePersistentRocksDBNode = "SurrealDB Community; one persistent RocksDB node";
    private const string SingleNodeAcknowledgedWriteCommitDurabilityNotFault = "single-node acknowledged write; commit durability not fault-qualified";
    private const string SingleNodeCommittedReads = "single-node committed reads";
    private const string SurrealDBHTTPSqlJSON = "SurrealDB HTTP /sql JSON";
    private const string RootBasicAuthentication = "root Basic authentication";
    private const int SingleResultCardinality = 1;
    private const string SingleSurrealDBCommunityNodeNoReplicationConfigured = "single SurrealDB Community node; no replication configured";
    private const string PersistentRocksDBPath = "persistent RocksDB path";
    private const string NativeDEFINETABLEIFNOTEXISTSSCHEMALESSFormatTemplate = "DEFINE TABLE IF NOT EXISTS {0} SCHEMALESS;";
    private static readonly System.Text.CompositeFormat NativeDEFINETABLEIFNOTEXISTSSCHEMALESSFormat = System.Text.CompositeFormat.Parse(NativeDEFINETABLEIFNOTEXISTSSCHEMALESSFormatTemplate);
    private const int EmptyResultCount = 0;
    private const char VectorRecordPrefix = 'v';
    private const string HTTPSqlRocksDBPersistentBackend = "; HTTP /sql; RocksDB persistent backend";
    private const string SurrealDBProfileIsUnavailable = "SurrealDB profile is unavailable.";
    private const string NoANNIndexSurrealDBBruteForceKNN = "no ANN index; SurrealDB brute-force KNN";
    private const string SurrealDBSupportsOnlyExactAndNativeHNSWProfiles = "SurrealDB supports only exact and native HNSW profiles.";
    private const string NativeREMOVEINDEXONTABLEFormatTemplate = "REMOVE INDEX {0} ON TABLE {1};";
    private static readonly System.Text.CompositeFormat NativeREMOVEINDEXONTABLEFormat = System.Text.CompositeFormat.Parse(NativeREMOVEINDEXONTABLEFormatTemplate);
    private const string NativeDELETEFROMREMOVETABLEFormatTemplate = "DELETE FROM {0}; REMOVE TABLE {1};";
    private static readonly System.Text.CompositeFormat NativeDELETEFROMREMOVETABLEFormat = System.Text.CompositeFormat.Parse(NativeDELETEFROMREMOVETABLEFormatTemplate);
    private readonly IOptions<NativeComparisonExecutionOptions> execution = NativeComparisonExecutionOptions.Require(executionOptions);
    private NativeComparisonExecutionOptions Policy => execution.Value;
    private const string RunIdentityFormat = "N";
    private const string Table = "vector_doc";
    private const string InvalidResponse = "SurrealDbInvalidResponse";
    private const string IdKey = "id";
    private const string DistanceKey = "distance";
    private const string IndexNamePrefix = "keyload_hnsw_";
    private const int HnswEf = 200;
    private readonly string table = Table + SurrealDbNativeTokens.Token + Guid.Parse(runId).ToString(RunIdentityFormat);
    private readonly string index = IndexNamePrefix + Guid.Parse(runId).ToString(RunIdentityFormat);
    private int corpusCount;
    private string databaseVersion = SurrealDbNativeTokens.TokenUnverified;
    private bool ownsData;
    private bool ownsIndex;
    /// <inheritdoc/>
    public string Name => SurrealDbNativeTokens.TokenSurrealDB;
    /// <inheritdoc/>
    public TargetProfile Profile { get; private set; } = new(SurrealDbNativeTokens.TokenSurrealDB, SurrealDbNativeTokens.TokenUnverified, SurrealDBCommunityOnePersistentRocksDBNode, SingleNodeAcknowledgedWriteCommitDurabilityNotFault, SingleNodeCommittedReads, SurrealDBHTTPSqlJSON, RootBasicAuthentication, image)
    {
        Cluster = new(SingleResultCardinality, SingleResultCardinality, SingleSurrealDBCommunityNodeNoReplicationConfigured, [PersistentRocksDBPath])
    };

    /// <inheritdoc/>
    public bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode) => indexKind is VectorIndexKind.Exact or VectorIndexKind.Hnsw && Enum.IsDefined(queryMode);
    /// <inheritdoc/>
    public async Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        await VerifyServerAsync(cancellationToken).ConfigureAwait(false);
        await SurrealDbSqlTransport.ExecuteAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeDEFINETABLEIFNOTEXISTSSCHEMALESSFormat, table), Policy, cancellationToken).ConfigureAwait(false);
        ownsData = true;
        var batch = new List<VectorDocument>(Policy.WriteBatchCapacity);
        var count = EmptyResultCount;
        await foreach (var document in documents.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (document.Embedding.Length == EmptyResultCount || document.Number < EmptyResultCount || !document.Id.StartsWith(VectorRecordPrefix))
            {
                throw new InvalidDataException(SurrealDbNativeTokens.TokenSurrealDbInvalidSeedDocument);
            }

            batch.Add(document);
            count++;
            if (batch.Count == Policy.WriteBatchCapacity)
            {
                await IngestBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count != EmptyResultCount)
        {
            await IngestBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        corpusCount = count;
        Profile = Profile with
        {
            Version = databaseVersion + HTTPSqlRocksDBPersistentBackend
        };
        return count;
    }

    /// <inheritdoc/>
    public async Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Supports(profile.IndexKind, profile.QueryMode))
        {
            throw new NotSupportedException(SurrealDBProfileIsUnavailable);
        }

        ProfileIndex = profile.IndexKind;
        if (profile.IndexKind == VectorIndexKind.Exact)
        {
            var parameters = new Dictionary<string, string>();
            Policy.RecordEvidence(parameters);
            return new(VectorIndexKind.Exact, NoANNIndexSurrealDBBruteForceKNN, parameters, EmptyResultCount);
        }

        if (profile.IndexKind != VectorIndexKind.Hnsw)
        {
            throw new NotSupportedException(SurrealDBSupportsOnlyExactAndNativeHNSWProfiles);
        }

        ownsIndex = true;
        return await SurrealDbVectorIndex.BuildAsync(http, table, index, profile, Policy, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK, VectorQueryMode mode, CancellationToken cancellationToken)
    {
        var sql = SurrealDbVectorProtocol.SearchSql(table, query.Span, topK, mode, ProfileIndex, HnswEf);
        using var response = await SurrealDbSqlTransport.QueryAsync(http, sql, Policy, cancellationToken).ConfigureAwait(false);
        var result = SurrealDbVectorProtocol.SingleResult(response.RootElement);
        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(InvalidResponse);
        }

        return result.EnumerateArray().Select(row => new VectorNeighbor(SurrealDbVectorProtocol.ReadRecordId(table, row.GetProperty(IdKey)), SurrealDbVectorProtocol.ReadRequiredDouble(row, DistanceKey))).ToArray();
    }

    /// <inheritdoc/>
    public async Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken)
    {
        var sql = SurrealDbVectorProtocol.ExplainSql(table, query.Span, mode, ProfileIndex, HnswEf);
        using var response = await SurrealDbSqlTransport.QueryAsync(http, sql, Policy, cancellationToken).ConfigureAwait(false);
        var nativePlan = SurrealDbVectorProtocol.SingleResult(response.RootElement);
        if (ProfileIndex == VectorIndexKind.Hnsw)
        {
            SurrealDbVectorPlan.Validate(nativePlan, index, HnswEf, query.Length, mode);
        }
        return nativePlan.GetRawText();
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        await SurrealDbSqlTransport.ExecuteAsync(http, SurrealDbVectorProtocol.UpdateSql(table, update.Id, update.Embedding.Span), Policy, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (ownsData)
            {
                using var timeout = new CancellationTokenSource(Policy.CleanupTimeout);
                if (ownsIndex)
                {
                    await SurrealDbSqlTransport.ExecuteAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeREMOVEINDEXONTABLEFormat, index, table), Policy, timeout.Token).ConfigureAwait(false);
                }

                await SurrealDbSqlTransport.ExecuteAsync(http, string.Format(System.Globalization.CultureInfo.InvariantCulture, NativeDELETEFROMREMOVETABLEFormat, table, table), Policy, timeout.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            http.Dispose();
        }
    }

    private VectorIndexKind ProfileIndex { get; set; } = VectorIndexKind.Exact;

    private async Task VerifyServerAsync(CancellationToken cancellationToken)
    {
        databaseVersion = await SurrealDbServer.VerifyAsync(http, Policy, cancellationToken).ConfigureAwait(false);
    }

    private async Task IngestBatchAsync(List<VectorDocument> batch, CancellationToken cancellationToken)
    {
        var sql = SurrealDbVectorProtocol.CreateBatchSql(table, batch);
        await SurrealDbSqlTransport.ExecuteAsync(http, sql, Policy, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken) => SurrealDbVectorStorage.ReadbackAsync(http, table, Policy, corpusCount, cancellationToken);
    /// <inheritdoc/>
    public Task<VectorReadback?> ReadAsync(string id, CancellationToken cancellationToken) => SurrealDbVectorStorage.ReadAsync(http, table, Policy, id, cancellationToken);
}
