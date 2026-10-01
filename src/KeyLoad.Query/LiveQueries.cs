using System.Diagnostics;
using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Query;

public sealed partial class QueryEngine
{
    private sealed record LiveCursorClaims(string Purpose, string QueryHash, string ChangeCursor);
    private AstQueryRequest LiveRequest(AstQueryRequest request)
    {
        request = QueryValidation.Normalize(request, database.Limits);
        if (request.Cursor is not null || request.Query.Order.Length != 0 || request.Query.Explain)
            throw Errors.Fail(ErrorCode.UnsupportedCapability, "The live-query profile requires an unordered complete scalar result set.");
        return request;
    }
    public LiveQuerySnapshot StartLiveQuery(string principalId, StartLiveQueryRequest request)
    {
        if (!admission.Wait(0)) throw Errors.Fail(ErrorCode.ResourceExhausted, "The query concurrency budget is exhausted.");
        try
        {
            var query = LiveRequest(request.Query); var hash = QueryHash(query); var started = Stopwatch.StartNew();
            return database.WithQueryView(principalId, query.Partition, query.Query.Collection, (view, principal, resource) =>
            {
                var capture = database.CaptureChangeFeedCursor(view, principal, query.Partition, query.Query.Collection);
                var page = ExecuteView(view, principal, resource, query, hash, started);
                if (page.Cursor is not null) throw Errors.Fail(ErrorCode.BudgetExceeded, "The live-query snapshot exceeds its complete result-set budget.");
                return new LiveQuerySnapshot(page.Rows, database.Sign(new LiveCursorClaims("scalar-live-query", hash, capture.Cursor)),
                    capture.Tail, page.CutPosition);
            });
        }
        finally { admission.Release(); }
    }
    public LiveQueryPage ReadLiveQuery(string principalId, ReadLiveQueryRequest request)
    {
        if (!admission.Wait(0)) throw Errors.Fail(ErrorCode.ResourceExhausted, "The query concurrency budget is exhausted.");
        try
        {
            var query = LiveRequest(request.Query); var hash = QueryHash(query); var started = Stopwatch.StartNew();
            return database.WithQueryView(principalId, query.Partition, query.Query.Collection, (view, principal, resource) =>
            {
                Bind(principal, resource, query);
                var claims = database.Verify<LiveCursorClaims>(request.Cursor);
                if (claims.Purpose != "scalar-live-query" || claims.QueryHash != hash)
                    throw Errors.Fail(ErrorCode.TokenInvalidated, "The live-query cursor belongs to a different query.");
                bool Matches(DocumentRecord? record)
                {
                    if (record is null || record.Deleted) return false;
                    if (query.Query.Filter is null) return true;
                    using var json = JsonDocument.Parse(record.Json);
                    return PredicateEvaluator.Evaluate(query.Query.Filter, record, json.RootElement, query.Parameters) == true;
                }
                var page = database.ReadChangeFeedView<LiveQueryChange>(view, principal,
                    new(query.Partition, query.Query.Collection, claims.ChangeCursor, Limit: request.Limit, MaxBytes: request.MaxBytes), (schema, change) =>
                    {
                        if (started.Elapsed > TimeSpan.FromSeconds(database.Limits.QueryDeadlineSeconds))
                            throw Errors.Fail(ErrorCode.BudgetExceeded, "The live-query deadline is exceeded.");
                        var matchesAfter = Matches(change.After);
                        if (!matchesAfter && !Matches(change.Before)) return null;
                        return new(change.Sequence, change.Commit, matchesAfter ? LiveQueryChangeKind.Upsert : LiveQueryChangeKind.Remove,
                            change.After.Reference, change.After.Revision,
                            matchesAfter ? Project(principal, schema, change.After, query.Query.Projection) : null);
                    });
                return new LiveQueryPage(page.Changes, database.Sign(new LiveCursorClaims("scalar-live-query", hash, page.Cursor)),
                    page.ThroughSequence, page.HasMore, page.CutPosition);
            });
        }
        finally { admission.Release(); }
    }
}
