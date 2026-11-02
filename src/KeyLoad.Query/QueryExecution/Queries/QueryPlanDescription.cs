using System.Text.Json;

namespace KeyLoad.Query.QueryExecution;

internal static class QueryPlanDescription
{
    private const string PointLookup = "point-lookup";
    private const string PrimaryRange = "primary-key-range-seek";
    private const string IndexPrefix = "index-prefix-seek";
    private const string IndexRange = "index-range-seek";
    private const string FullScan = "bounded-full-scan";
    private const string Visibility = "row-authorization";
    private const string Filter = "residual-filter";
    private const string Sort = "top-n-heap";
    private const string Projection = "projection";
    private const string Page = "page";
    private const int InitialSequence = 0;

    internal static QueryPage Create(AstQueryRequest request, QueryAccessPlan access, long cut, int offset)
    {
        var query = request.Query;
        var scan = access.Kind switch
        {
            QueryAccessKind.Point => PointLookup,
            QueryAccessKind.PrimaryRange => PrimaryRange,
            QueryAccessKind.IndexPrefix => IndexPrefix,
            QueryAccessKind.IndexRange => IndexRange,
            _ => FullScan
        };
        var stages = new List<string> { scan, Visibility };
        if (query.Filter is not null)
        {
            stages.Add(Filter);
        }
        stages.AddRange([Sort, Projection, Page]);
        var plan = new QueryExecutionPlan(access.AccessPath, request.Partition.AtomicPartitionId, cut,
            scan, access.IndexName, [.. access.IndexFields ?? []], access.AfterKey is not null, access.UntilKey is not null,
            [.. PredicateEvaluator.Fields(query.Filter).Distinct(StringComparer.Ordinal)], query.Order,
            Math.Min(int.MaxValue, (long)offset + query.Limit), query.Limit, offset,
            query.Projection, [.. stages]);
        return new([new(QueryEngine.ExplainId, InitialSequence, JsonSerializer.Serialize(plan, JsonDefaults.Options))],
            null, cut, access.AccessPath);
    }
}
