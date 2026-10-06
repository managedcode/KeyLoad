using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons.Targets;

/// <summary>Compares document and directed graph operations against an isolated Neo4j Community label on one node.</summary>
/// <param name="http">The authenticated Neo4j Query API client; the target disposes it.</param>
/// <param name="runId">Guid-formatted run identifier used to isolate node labels and constraints.</param>
/// <param name="image">Neo4j image reference recorded in the target profile.</param>
/// <param name="lifecycleOptions">Centrally validated native lifecycle policy.</param>
/// <param name="nativeExecutionOptions">Centrally validated native adapter execution policy.</param>
public sealed class Neo4jTarget(HttpClient http, string runId, string image,
    IOptions<ComparisonLifecycleOptions> lifecycleOptions, IOptions<NativeComparisonExecutionOptions> nativeExecutionOptions) : IComparisonTarget
{
    private const string InitializeAsyncCREATECONSTRAINTText = "CREATE CONSTRAINT ";
    private const string InitializeAsyncIdFORNText = "_id FOR (n:";
    private const string InitializeAsyncREQUIRENIdISUNIQUEText = ") REQUIRE n.id IS UNIQUE";
    private const string InitializeAsyncUNWINDDocumentsASDCREATENText = "UNWIND $documents AS d CREATE (n:";
    private const string InitializeAsyncIdDIdJsonDJsonText = " {id:d.id,json:d.json})";
    private const string InitializeAsyncUNWINDEdgesASEMATCHAText = "UNWIND $edges AS e MATCH (a:";
    private const string InitializeAsyncIdEFromBText = " {id:e.from}),(b:";
    private const string InitializeAsyncIdEToCREATEALINKSBText = " {id:e.to}) CREATE (a)-[:LINKS]->(b)";
    private const string DisposeAsyncMATCHNText = "MATCH (n:";
    private const string DisposeAsyncDETACHDELETENText = ") DETACH DELETE n";
    private const string DisposeAsyncDROPCONSTRAINTText = "DROP CONSTRAINT ";
    private const string DisposeAsyncIdIFEXISTSText = "_id IF EXISTS";

    private readonly NativeComparisonExecutionOptions execution = NativeComparisonExecutionOptions.Require(nativeExecutionOptions).Value;

    private const string BenchmarkToken = "Benchmark_";
    private const string Neo4jToken = "Neo4j";
    private const string UnverifiedToken = "unverified";
    private const string CommunitySingleNodeHeapMiBPageCacheMiBToken = "Community; single node; heap 512 MiB, page cache 256 MiB";
    private const string LocalTransactionAcknowledgementContract = "local committed transaction; no synchronous replicas; durability not fault-qualified";
    private const string CommittedPrimaryIndexedIDsAndBoundedDirectedReachabilityContractText = "committed primary; indexed IDs and bounded directed reachability";
    private const string CypherQueryAPIV2HTTPJSONToken = "Cypher Query API v2 / HTTP JSON";
    private const string Neo4jAdminNoRowFieldPolicyToken = "Neo4j admin; no row/field policy";

    private const string RunIdentityFormat = "N";

    private const string DataProperty = "data";
    private const string ValuesProperty = "values";
    private const string CommunityEdition = "community";
    private const string CommunityRequired = "Neo4jCommunityEditionRequired";
    private const string SingleCommunityState = "single native Community node";
    private readonly string label = BenchmarkToken + Guid.Parse(runId).ToString(RunIdentityFormat);
    private bool ownsConstraint;
    private int depth;
    private int corpusCount;
    /// <summary>Gets the observed Neo4j version and declared single-node, local-transaction, and query profile.</summary>
    public TargetProfile Profile { get; private set; } = new(Neo4jToken, UnverifiedToken, CommunitySingleNodeHeapMiBPageCacheMiBToken,
        LocalTransactionAcknowledgementContract, CommittedPrimaryIndexedIDsAndBoundedDirectedReachabilityContractText,
        CypherQueryAPIV2HTTPJSONToken, Neo4jAdminNoRowFieldPolicyToken, image);
    /// <summary>Reports support for point reads, document writes, and directed graph neighbor or traversal queries.</summary>
    /// <param name="scenario">The comparison scenario to check.</param>
    /// <returns><see langword="true"/> for a supported scenario; otherwise <see langword="false"/>.</returns>
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate
        or Scenario.DocumentDelete or Scenario.GraphNeighbors or Scenario.GraphTraverse;

    /// <summary>Reads the server version, creates an isolated uniqueness constraint, seeds documents and edges, and waits for indexes.</summary>
    /// <param name="dataset">The deterministic documents and directed graph edges to seed.</param>
    /// <param name="cancellationToken">A token that cancels HTTP queries and setup operations.</param>
    /// <returns>A task that completes after index readiness.</returns>
    public async Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
    {
        const string IndexedPrimaryDocumentReadsS1SeedsNodesOnlyWithNoGraphRelationshipsContractText = "indexed primary document reads; S1 seeds nodes only, with no graph relationships";
        const string ServerComponentsStatement = "CALL dbms.components() YIELD name,versions,edition WHERE name='Neo4j Kernel' RETURN versions[0],edition";
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;
        const string VersionEditionSeparator = "; ";

        ArgumentNullException.ThrowIfNull(dataset);
        depth = dataset.Settings.GraphDepth;
        if (dataset.Settings is ScaledComparisonProfile)
        {
            Profile = Profile with { ReadContract = IndexedPrimaryDocumentReadsS1SeedsNodesOnlyWithNoGraphRelationshipsContractText };
        }
        corpusCount = dataset.Documents.Count;
        using var version = await QueryAsync(ServerComponentsStatement, null, cancellationToken);
        var row = Rows(version).EnumerateArray().Single();
        var edition = row[SingleItemCount].GetString();
        if (!string.Equals(edition, CommunityEdition, StringComparison.OrdinalIgnoreCase))
        {
            throw new ComparisonFailureException(CommunityRequired);
        }
        Profile = Profile with
        {
            Version = row[FirstElementIndex].GetString() + VersionEditionSeparator + edition,
            Cluster = new(SingleItemCount, SingleItemCount, SingleCommunityState, [CommunityEdition])
        };
        using (var constraint = await QueryAsync($"{InitializeAsyncCREATECONSTRAINTText}{label}{InitializeAsyncIdFORNText}{label}{InitializeAsyncREQUIRENIdISUNIQUEText}", null, cancellationToken))
        {
            Neo4jQueryProtocol.ValidateConstraintCreation(constraint.RootElement);
            ownsConstraint = true;
        }

        foreach (var batch in dataset.Documents.Chunk(execution.Neo4jSeedBatchSize))
        {
            await ExecuteAsync($"{InitializeAsyncUNWINDDocumentsASDCREATENText}{label}{InitializeAsyncIdDIdJsonDJsonText}",
                new { documents = batch.Select(document => new { id = document.Id, json = document.Json }).ToArray() }, cancellationToken);
        }

        foreach (var batch in dataset.Edges.Chunk(execution.Neo4jSeedBatchSize))
        {
            await ExecuteAsync($"{InitializeAsyncUNWINDEdgesASEMATCHAText}{label}{InitializeAsyncIdEFromBText}{label}{InitializeAsyncIdEToCREATEALINKSBText}",
                new { edges = batch.Select(edge => new { from = edge.From, to = edge.To }).ToArray() }, cancellationToken);
        }

        const string AwaitIndexesStatement = "CALL db.awaitIndexes({0})";
        await ExecuteAsync(string.Format(CultureInfo.InvariantCulture, AwaitIndexesStatement, execution.Neo4jMaximumExecutionTimeSeconds), null, cancellationToken);
    }

    private static JsonElement Rows(JsonDocument response) => response.RootElement.GetProperty(DataProperty).GetProperty(ValuesProperty);
    private async Task<JsonDocument> QueryAsync(string statement, object? parameters, CancellationToken cancellationToken)
    {
        const string DbNeo4jQueryV2Token = "db/neo4j/query/v2";

        using var response = await http.PostAsJsonAsync(DbNeo4jQueryV2Token, new { statement, parameters = parameters ?? new { }, maxExecutionTime = execution.Neo4jMaximumExecutionTimeSeconds }, cancellationToken);
        Neo4jQueryProtocol.RequireQueryStatus((int)response.StatusCode);
        JsonDocument? json = null;
        try
        {
            json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            Neo4jQueryProtocol.ValidateResponse((int)response.StatusCode, json.RootElement);
            return json;
        }
        catch (JsonException)
        {
            json?.Dispose();
            throw Neo4jQueryProtocol.Invalid();
        }
        catch (Exception)
        {
            json?.Dispose();
            throw;
        }
    }
    private async Task ExecuteAsync(string statement, object? parameters, CancellationToken cancellationToken)
    { using var response = await QueryAsync(statement, parameters, cancellationToken); }
    /// <summary>Opens a session that runs queries through this target’s Neo4j client.</summary>
    /// <param name="cancellationToken">A token accepted for the common target contract; session construction does not perform I/O.</param>
    /// <returns>A comparison session bound to this target.</returns>
    public Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken) => Task.FromResult<IComparisonSession>(new Session(this));
    /// <summary>Deletes this run’s nodes and uniqueness constraint, then disposes the owned HTTP client.</summary>
    /// <returns>A value task that completes after cleanup.</returns>
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(lifecycleOptions.Value.Neo4jCleanupTimeout);
        try
        {
            if (ownsConstraint)
            {
                await ExecuteAsync($"{DisposeAsyncMATCHNText}{label}{DisposeAsyncDETACHDELETENText}", null, timeout.Token);
                await ExecuteAsync($"{DisposeAsyncDROPCONSTRAINTText}{label}{DisposeAsyncIdIFEXISTSText}", null, timeout.Token);
            }
        }
        finally { http.Dispose(); }
    }

    /// <summary>Executes one comparison session’s document and directed graph queries.</summary>
    private sealed class Session(Neo4jTarget target) : IComparisonSession
    {
    private const string ReadCorpusAsyncMATCHNText = "MATCH (n:";
    private const string ReadCorpusAsyncWHEREAfterISNULLORNIdAfterRETURNNIdNJsonORDERBYNIdLIMITText = ") WHERE $after IS NULL OR n.id > $after RETURN n.id,n.json ORDER BY n.id LIMIT ";
    private const string ReadAsyncIdIdRETURNNJsonText = " {id:$id}) RETURN n.json";
    private const string ExecuteAsyncMATCHAText = "MATCH (a:";
    private const string ExecuteAsyncIdIdLINKSText = " {id:$id})-[:LINKS*1..";
    private const string ExecuteAsyncBText = "]->(b:";
    private const string ExecuteAsyncWHEREBIdIdRETURNDISTINCTBIdORDERBYBIdText = ") WHERE b.id<>$id RETURN DISTINCT b.id ORDER BY b.id";

        public async IAsyncEnumerable<FoundDocument> ReadCorpusAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            const int NoObservedItems = 0;
            const int NoItems = 0;
            const int FirstElementIndex = 0;
            const int SingleItemCount = 1;
            const string ScaledCorpusReadbackCountMismatchDetail = "ScaledCorpusReadbackCountMismatch";

            string? after = null;
            var seen = NoObservedItems;
            while (true)
            {
                using var response = await target.QueryAsync($"{ReadCorpusAsyncMATCHNText}{target.label}{ReadCorpusAsyncWHEREAfterISNULLORNIdAfterRETURNNIdNJsonORDERBYNIdLIMITText}{target.execution.ReadbackBatchCapacity}",
                    new { after }, cancellationToken);
                var rows = Rows(response);
                if (rows.GetArrayLength() == NoItems)
                {
                    break;
                }
                foreach (var row in rows.EnumerateArray())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    after = row[FirstElementIndex].GetString();
                    seen++;
                    yield return new(after!, row[SingleItemCount].GetString()!);
                }
            }
            if (seen != target.corpusCount)
            {
                throw new ComparisonFailureException(ScaledCorpusReadbackCountMismatchDetail);
            }
        }

        public async Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        {
            const int NoItems = 0;
            const int FirstElementIndex = 0;

            using var response = await target.QueryAsync($"{ReadCorpusAsyncMATCHNText}{target.label}{ReadAsyncIdIdRETURNNJsonText}", new { id = document.Id }, cancellationToken);
            var rows = Rows(response);
            return rows.GetArrayLength() == NoItems ? null : new(document.Id, rows[FirstElementIndex][FirstElementIndex].GetString()!);
        }
        public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            const int SingleItemCount = 1;
            const int FirstElementIndex = 0;

            switch (scenario)
            {
                case Scenario.PointRead:
                    return new(Document: await ReadAsync(document, cancellationToken));
                case Scenario.DocumentWrite:
                case Scenario.DocumentUpdate:
                case Scenario.DocumentDelete:
                    return await Neo4jMutationOperations.ExecuteAsync(target.QueryAsync, target.label, scenario, document, cancellationToken);
                case Scenario.GraphNeighbors:
                case Scenario.GraphTraverse:
                    var hops = scenario == Scenario.GraphNeighbors ? SingleItemCount : target.depth;
                    using (var response = await target.QueryAsync($"{ExecuteAsyncMATCHAText}{target.label}{ExecuteAsyncIdIdLINKSText}{hops}{ExecuteAsyncBText}{target.label}{ExecuteAsyncWHEREBIdIdRETURNDISTINCTBIdORDERBYBIdText}", new { id = document.Id }, cancellationToken))
                    {
                        return new(Vertices: ImmutableCollectionsMarshal.AsImmutableArray(Rows(response).EnumerateArray()
                            .Select(row => row[FirstElementIndex].GetString()!).ToArray()));
                    }

                default:
                    throw new NotSupportedException();
            }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
