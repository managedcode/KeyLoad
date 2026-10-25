using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    private readonly IQueryReadCutObserver? readCutObserver;

    internal global::ManagedCode.Communication.Result<QueryReadCutObservation?> CaptureQueryReadCut(PrincipalRecord principal, ResourceDefinition resource,
        AstQueryRequest request, string hash, QueryPage page, ReadExecutionBudget budget)
        => readCutObserver is null || request.Query.Explain ? global::ManagedCode.Communication.Result<QueryReadCutObservation?>.FailNotFound(nameof(CaptureQueryReadCut))
            : global::ManagedCode.Communication.Result<QueryReadCutObservation?>.Succeed(((QueryReadCutObservation?)(QueryReadCutObservationDispatch.Capture(database, principal, resource, request, hash, page, budget))));

    internal void PublishQueryReadCut(QueryReadCutObservation? observation, ReadExecutionBudget budget)
        => QueryReadCutObservationDispatch.Observe(readCutObserver, observation, budget);

    /// <summary>Creates the same query owner with an optional synchronous private read observer.</summary>
    /// <param name="database">Actual node-local owner.</param>
    /// <param name="options">Centrally validated existing query settings.</param>
    /// <param name="searchEngine">Existing shared search executor.</param>
    /// <param name="readCutObserver">Enabled private observation, or null.</param>
    public QueryEngine(DatabaseEngine database, IOptions<QueryExecutionOptions> options,
        SearchEngine? searchEngine, IQueryReadCutObserver? readCutObserver)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(options);
        execution = options.Value;
        execution.Validate();
        executionOptions = options;
        cursorLifetime = execution.CursorLifetime;
        this.database = database;
        this.readCutObserver = readCutObserver;
        graphSearch = searchEngine ?? new SearchEngine(database, options);
        liveQueries = new(database, this);
        modelQueries = new(database, this);
    }
}
