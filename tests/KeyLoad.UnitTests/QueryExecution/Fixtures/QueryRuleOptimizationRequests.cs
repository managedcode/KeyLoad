using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryRuleOptimizationRequests
{
    private const string RevisionOneSql = "revision = 1";
    private const string OpenRevisionOneSql = "status = 'open' AND revision = 1";
    private const string OpenRevisionTwoSql = "revision = 2 AND status = 'open'";
    private const string RawShadowSql = "revision = 99";
    private const string NestedSql = "nested.[@revision] = 7";
    private const string SelectSqlPrefix = "SELECT TOP (2) status,rank FROM ";
    private const string WhereSql = " WHERE ";
    private const string OrderSql = " ORDER BY rank";
    private const string OrSqlSeparator = ") OR (";

    internal static QueryRequest Sql(PartitionRef partition, int collection, int shape, bool reference, bool consent = true)
    {
        var predicate = shape switch
        {
            QueryRuleOptimizationData.RevisionOneShape => RevisionOneSql,
            QueryRuleOptimizationData.OpenRevisionOneShape => OpenRevisionOneSql,
            QueryRuleOptimizationData.OpenRevisionTwoShape => OpenRevisionTwoSql,
            QueryRuleOptimizationData.RawShadowShape => RawShadowSql,
            _ => NestedSql
        };
        if (reference)
        {
            predicate = "(" + predicate + OrSqlSeparator + predicate + ")";
        }
        return new(partition, SelectSqlPrefix + QueryRuleOptimizationData.Collections[collection]
            + WhereSql + predicate + OrderSql, AllowFullScan: consent);
    }
    internal static AstQueryRequest Ast(PartitionRef partition, int collection, int shape, bool reference, bool consent = true)
    {
        Predicate revision = new Comparison(new FieldOperand(QueryRuleOptimizationData.RevisionPath), QueryRuleOptimizationData.EqualsOperator,
            ValueOperand.Create(shape == QueryRuleOptimizationData.OpenRevisionTwoShape ? 2m : shape == QueryRuleOptimizationData.RawShadowShape ? 99m : 1m));
        Predicate status = new Comparison(new FieldOperand(QueryRuleOptimizationData.StatusPath), QueryRuleOptimizationData.EqualsOperator, ValueOperand.Create("open"));
        var filter = shape switch
        {
            QueryRuleOptimizationData.OpenRevisionOneShape => new Logical(status, QueryRuleOptimizationData.AndOperator, revision),
            QueryRuleOptimizationData.OpenRevisionTwoShape => new Logical(revision, QueryRuleOptimizationData.AndOperator, status),
            QueryRuleOptimizationData.NestedShape => (Predicate)new Comparison(new FieldOperand(QueryRuleOptimizationData.NestedPath), QueryRuleOptimizationData.EqualsOperator, ValueOperand.Create(7m)),
            _ => revision
        };
        return new(partition, new(QueryRuleOptimizationData.Collections[collection], null,
            [new(QueryRuleOptimizationData.StatusPath, "status"), new(QueryRuleOptimizationData.RankPath, "rank")],
            reference ? new Logical(filter, QueryRuleOptimizationData.OrOperator, filter) : filter, [new(QueryRuleOptimizationData.RankPath, false)], QueryRuleOptimizationData.PageSize), AllowFullScan: consent);
    }
    internal static AstQueryRequest Builder(PartitionRef partition, int collection, int shape, bool reference)
    {
        var query = KeyLoadQuery.From<QueryRuleOptimizationRow>(partition, QueryRuleOptimizationData.Collections[collection], UnitClientOptions.Translation());
        query = shape switch
        {
            QueryRuleOptimizationData.RevisionOneShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 1L),
            QueryRuleOptimizationData.OpenRevisionOneShape => query.Where(row => row.Status == "open" && QueryFunctions.DocumentRevision(row) == 1L),
            QueryRuleOptimizationData.OpenRevisionTwoShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 2L && row.Status == "open"),
            QueryRuleOptimizationData.RawShadowShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 99L),
            _ => query.Where(row => row.Nested.Revision == 7m)
        };
        var request = query.OrderBy(row => row.Rank).Select(row => new
        {
            status = row.Status,
            rank = row.Rank
        })
            .Take(QueryRuleOptimizationData.PageSize).ToRequest(true);
        return !reference ? request : request with
        {
            Query = request.Query with
            {
                Filter = new Logical(request.Query.Filter!, QueryRuleOptimizationData.OrOperator, request.Query.Filter!)
            }
        };
    }
}
