using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.Features.ChangeFeeds;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

/// <summary>Executes authorized bounded SQL and AST queries.</summary>
public sealed partial class QueryEngine
{
    private const int InitialSequence = 0;
    private const long NoRetainedBytesLong = 0L;

    internal const string ExplainId = "explain";
    internal const string ResultLimitExceeded = "The query result byte budget is exceeded.";
    private readonly TimeSpan cursorLifetime;
    private readonly QueryExecutionOptions execution;
    private readonly IOptions<QueryExecutionOptions> executionOptions;
    internal QueryExecutionOptions Execution => execution;
    private readonly DatabaseEngine database;
    internal DatabaseEngine PartitionQueryOwner => database;
    private readonly SearchEngine graphSearch;
    private readonly LiveQueryExecutor liveQueries;
    private readonly ModelQueryExecutor modelQueries;

    /// <summary>Creates a query engine over one node-local database.</summary>
    /// <param name="database">Database owning query reads and admission.</param>
    /// <param name="options">Centrally validated continuation settings, frozen for this execution owner.</param>
    /// <param name="searchEngine">The shared authorized search executor.</param>
    public QueryEngine(DatabaseEngine database, IOptions<QueryExecutionOptions> options, SearchEngine? searchEngine = null)
        : this(database, options, searchEngine, null)
    {
    }

    /// <summary>Executes a bounded SQL query against one authorized read cut.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">SQL query and optional continuation.</param>
    /// <param name="timeProvider">Optional operation clock for deadline and cursor expiry.</param>
    /// <param name="cancellationToken">Caller cancellation for parsing, scanning, sorting and projection.</param>
    /// <returns>A bounded page with authorized continuation metadata.</returns>
    public QueryPage Execute(string principalId, QueryRequest request, TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
        => Execute(principalId, budget => global::KeyLoad.Query.Features.QueryExecution.ScriptDomQueryAdapter.Lower(
            global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.Parse(request.Sql, database.OperationLimitsOptions, budget.Check, cancellationToken),
            request, database.OperationLimitsOptions, executionOptions, budget, cancellationToken), timeProvider, cancellationToken);

    /// <summary>Executes a bounded Q1.Search.v1 statement through the canonical graph-search engine.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">Versioned SQL graph-search request.</param>
    /// <param name="cancellationToken">Caller cancellation for bounded parse and search work.</param>
    /// <returns>The same graph-search result produced by the direct request contract.</returns>
    public Task<GraphSearchResult> SearchSqlAsync(string principalId, SqlGraphSearchRequest request,
        CancellationToken cancellationToken = default)
        => SqlGraphSearchExecutor.ExecuteAsync(database, graphSearch, principalId, request, execution, cancellationToken);
    /// <summary>Executes a typed bounded query against one authorized read cut.</summary>
    /// <param name="principalId">Persisted database principal identifier.</param>
    /// <param name="request">Validated AST input and optional continuation.</param>
    /// <param name="timeProvider">Optional operation clock for deadline and cursor expiry.</param>
    /// <param name="cancellationToken">Caller cancellation for adaptation and execution.</param>
    /// <returns>A bounded page with authorized continuation metadata.</returns>
    public QueryPage ExecuteAst(string principalId, AstQueryRequest request, TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default) => Execute(principalId, _ => request, timeProvider, cancellationToken);
    /// <summary>Describes the supported query language and configured budgets.</summary>
    public QueryCapabilityManifest Capabilities => QueryCapabilityCatalog.Create(database.Limits, execution);
    private QueryPage Execute(string principalId, Func<ReadExecutionBudget, AstQueryRequest> adapt, TimeProvider? timeProvider,
        CancellationToken cancellationToken)
    {
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, timeProvider ?? database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        budget.Check();
        var request = QueryValidation.Normalize(adapt(budget), database.Limits, execution);
        var query = request.Query;
        if (query.ModelSource is not null)
        {
            return modelQueries.Execute(principalId, request, budget);
        }
        var hash = QueryHash(request);
        budget.Check();
        return RelationalInnerJoinDispatch.Execute(this, principalId, request, hash, budget,
            timeProvider ?? database.EvaluationClock);
    }

    internal static string QueryHash(AstQueryRequest request) => QueryRequestIdentity.Hash(request);
    internal void Bind(PrincipalRecord principal, ResourceDefinition resource, AstQueryRequest request)
        => QueryFieldAuthorization.Bind(database, principal, resource, request);
    internal QueryPage ExecuteView(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        AstQueryRequest request, string hash, ReadExecutionBudget budget, TimeProvider clock)
    {
        var query = request.Query;
        budget.Check();
        Bind(principal, resource, request);
        var cursor = QueryCursorExecution.Resolve(database, view, principal, resource, request, hash, clock);
        var prepared = new PreparedQuery(query, cursor.Offset, database.Limits.MaxScanRecords);
        var accessPath = new QueryCandidateReader(database, view, principal, resource, request, budget, document =>
        {
            budget.Check();
            if (query.Explain)
            {
                return;
            }
            using var json = JsonDocument.Parse(document.Json);
            if (query.Filter is null || PredicateEvaluator.Evaluate(query.Filter, document, json.RootElement,
                    request.Parameters, prepared.Paths) == true)
            {
                budget.Check();
                prepared.Consider(document, json.RootElement);
            }
        }, visibilitySourceEpoch: cursor.SourceEpoch).Visit();
        if (query.Explain)
        {
            var explain = new QueryPage([new(ExplainId, InitialSequence, JsonSerializer.Serialize(new { AccessPath = accessPath,
                AtomicPartition = request.Partition.AtomicPartitionId, ScanBudget = database.Limits.MaxScanRecords },
                JsonDefaults.Options))], null, cursor.Cut, accessPath);
            budget.CheckResult(explain);
            return explain;
        }
        return BuildPage(principal, resource, request, hash, cursor, prepared, accessPath, budget, clock);
    }

    private QueryPage BuildPage(PrincipalRecord principal, ResourceDefinition resource, AstQueryRequest request,
        string hash, QueryCursorExecution.State cursor, PreparedQuery prepared, string accessPath, ReadExecutionBudget budget, TimeProvider clock)
    {
        var query = request.Query;
        var rows = new List<QueryRow>();
        var minimumResultBytes = NoRetainedBytesLong;
        foreach (var row in prepared.Page(cursor.Offset, query.Limit, budget))
        {
            budget.Check();
            var projected = Project(principal, resource, row.Document, query.Projection, prepared.Paths);
            minimumResultBytes += Encoding.UTF8.GetByteCount(projected.Json);
            if (minimumResultBytes > budget.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResultLimitExceeded);
            }
            rows.Add(projected);
        }
        var next = cursor.Offset + rows.Count;
        var token = QueryCursorExecution.SignNext(database, cursorLifetime, principal, resource, hash,
            cursor, next, prepared.EligibleCount, clock);
        var result = new QueryPage(rows.ToImmutableArray(), token, cursor.Cut, accessPath);
        budget.CheckResult(result);
        return result;
    }
    internal QueryRow Project(PrincipalRecord principal, ResourceDefinition resource, DocumentRecord document,
        ImmutableArray<Selection> selections, IReadOnlyDictionary<string, string[]>? paths = null)
        => QueryRowProjection.Project(database, principal, resource, document, selections, paths);
}
