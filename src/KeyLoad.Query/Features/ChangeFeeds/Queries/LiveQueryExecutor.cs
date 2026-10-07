using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.ChangeFeeds;

/// <summary>Owns live-query snapshot and delta mapping over the caller's existing database cut.</summary>
internal sealed class LiveQueryExecutor(DatabaseEngine database, QueryEngine queryEngine)
{
    private const int EmptyElementCount = 0;
    private const string LiveQuerySnapshotExceedsItsCompleteResultSetBudgetDetail = "The live-query snapshot exceeds its complete result-set budget.";
    private const string LiveQueryCursorBelongsToADifferentQueryDetail = "The live-query cursor belongs to a different query.";
    private const string LiveQueryProfileRequiresAnUnorderedCompleteScalarResultSetDetail = "The live-query profile requires an unordered complete scalar result set.";

    internal LiveQuerySnapshot Start(string principalId, StartLiveQueryRequest request,
        TimeProvider? timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, timeProvider ?? database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, queryEngine.Execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        var query = LiveRequest(request.Query);
        var hash = QueryEngine.QueryHash(query);
        budget.Check();
        return database.WithQueryView(principalId, query.Partition, query.Query.Collection,
            (view, principal, resource) => StartView(view, principal, resource, query, hash, budget,
                timeProvider ?? database.EvaluationClock));
    }

    internal LiveQueryPage Read(string principalId, ReadLiveQueryRequest request,
        TimeProvider? timeProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, timeProvider ?? database.EvaluationClock, cancellationToken);
        QueryResultBudgetPolicy.Constrain(budget, queryEngine.Execution);
        budget.Check();
        using var reservation = database.AdmitQuery(cancellationToken);
        var query = LiveRequest(request.Query);
        var hash = QueryEngine.QueryHash(query);
        var prepared = new PreparedQuery(query.Query, EmptyElementCount, database.Limits.MaxScanRecords);
        budget.Check();
        return database.WithQueryView(principalId, query.Partition, query.Query.Collection,
            (view, principal, resource) => ReadView(view, principal, resource, query, hash, prepared, request,
                budget));
    }

    private LiveQuerySnapshot StartView(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        AstQueryRequest query, string hash, ReadExecutionBudget budget, TimeProvider clock)
    {
        budget.Check();
        var capture = database.CaptureChangeFeedCursor(view, principal, query.Partition, query.Query.Collection);
        var page = queryEngine.ExecuteView(view, principal, resource, query, hash, budget, clock);
        if (page.Cursor is not null)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded,
                LiveQuerySnapshotExceedsItsCompleteResultSetBudgetDetail);
        }
        var snapshot = new LiveQuerySnapshot(page.Rows,
            database.Sign(new LiveCursorClaims(LiveCursorContract.Purpose, hash, capture.Cursor)), capture.Tail,
            page.CutPosition);
        budget.CheckResult(snapshot);
        return snapshot;
    }

    private LiveQueryPage ReadView(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        AstQueryRequest query, string hash, PreparedQuery prepared, ReadLiveQueryRequest request,
        ReadExecutionBudget budget)
    {
        budget.Check();
        queryEngine.Bind(principal, resource, query);
        var claims = database.Verify<LiveCursorClaims>(request.Cursor);
        if (claims.Purpose != LiveCursorContract.Purpose || claims.QueryHash != hash)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, LiveQueryCursorBelongsToADifferentQueryDetail);
        }
        var retained = queryEngine.Execution.MaximumResultBytes is null ? null : new LiveQueryResultByteAdmission(budget);
        var page = database.ReadChangeFeedView<LiveQueryChange>(view, principal,
            new(query.Partition, query.Query.Collection, claims.ChangeCursor, Limit: request.Limit,
                MaxBytes: request.MaxBytes),
            (schema, change) => MapChange(principal, schema, change, query, prepared, budget),
            retained is null ? null : retained.Accept);
        var result = new LiveQueryPage(page.Changes,
            database.Sign(new LiveCursorClaims(LiveCursorContract.Purpose, hash, page.Cursor)), page.ThroughSequence,
            page.HasMore, page.CutPosition);
        budget.CheckResult(result);
        return result;
    }

    private LiveQueryChange? MapChange(PrincipalRecord principal, ResourceDefinition schema,
        AuthorizedDocumentChange change, AstQueryRequest query, PreparedQuery prepared, ReadExecutionBudget budget)
    {
        budget.Check();
        var matchesAfter = Matches(change.After, query, prepared);
        if (!matchesAfter && !Matches(change.Before, query, prepared))
        {
            return null;
        }
        return new(change.Sequence, change.Commit, matchesAfter ? LiveQueryChangeKind.Upsert : LiveQueryChangeKind.Remove,
            change.After.Reference, change.After.Revision,
            matchesAfter ? queryEngine.Project(principal, schema, change.After, query.Query.Projection,
                prepared.Paths) : null);
    }

    private static bool Matches(DocumentRecord? record, AstQueryRequest query, PreparedQuery prepared)
    {
        if (record is null || record.Deleted)
        {
            return false;
        }
        if (query.Query.Filter is null)
        {
            return true;
        }
        using var json = JsonDocument.Parse(record.Json);
        return PredicateEvaluator.Evaluate(query.Query.Filter, record, json.RootElement, query.Parameters,
            prepared.Paths) == true;
    }

    private AstQueryRequest LiveRequest(AstQueryRequest request)
    {
        request = QueryValidation.Normalize(request, database.Limits, queryEngine.Execution);
        if (request.Cursor is not null || request.Query.Order.Length != EmptyElementCount || request.Query.Explain)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability,
                LiveQueryProfileRequiresAnUnorderedCompleteScalarResultSetDetail);
        }
        return request;
    }
}
