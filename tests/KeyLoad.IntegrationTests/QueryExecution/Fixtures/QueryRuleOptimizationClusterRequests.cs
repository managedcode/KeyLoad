using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationClusterRequests
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
            QueryRuleOptimizationClusterData.RevisionOneShape => RevisionOneSql,
            QueryRuleOptimizationClusterData.OpenRevisionOneShape => OpenRevisionOneSql,
            QueryRuleOptimizationClusterData.OpenRevisionTwoShape => OpenRevisionTwoSql,
            QueryRuleOptimizationClusterData.RawShadowShape => RawShadowSql,
            _ => NestedSql
        };
        if (reference)
        {
            predicate = "(" + predicate + OrSqlSeparator + predicate + ")";
        }
        return new(partition, SelectSqlPrefix + QueryRuleOptimizationClusterData.Collections[collection]
            + WhereSql + predicate + OrderSql, AllowFullScan: consent);
    }
    internal static AstQueryRequest Ast(PartitionRef partition, int collection, int shape, bool reference, bool consent = true)
    {
        Predicate revision = new Comparison(new FieldOperand(QueryRuleOptimizationClusterData.RevisionPath), QueryRuleOptimizationClusterData.EqualsOperator,
            ValueOperand.Create(shape == QueryRuleOptimizationClusterData.OpenRevisionTwoShape ? 2m : shape == QueryRuleOptimizationClusterData.RawShadowShape ? 99m : 1m));
        Predicate status = new Comparison(new FieldOperand(QueryRuleOptimizationClusterData.StatusPath), QueryRuleOptimizationClusterData.EqualsOperator, ValueOperand.Create("open"));
        var filter = shape switch
        {
            QueryRuleOptimizationClusterData.OpenRevisionOneShape => new Logical(status, QueryRuleOptimizationClusterData.AndOperator, revision),
            QueryRuleOptimizationClusterData.OpenRevisionTwoShape => new Logical(revision, QueryRuleOptimizationClusterData.AndOperator, status),
            QueryRuleOptimizationClusterData.NestedShape => (Predicate)new Comparison(new FieldOperand(QueryRuleOptimizationClusterData.NestedPath), QueryRuleOptimizationClusterData.EqualsOperator, ValueOperand.Create(7m)),
            _ => revision
        };
        return new(partition, new(QueryRuleOptimizationClusterData.Collections[collection], null,
            [new(QueryRuleOptimizationClusterData.StatusPath, "status"), new(QueryRuleOptimizationClusterData.RankPath, "rank")],
            reference ? new Logical(filter, QueryRuleOptimizationClusterData.OrOperator, filter) : filter, [new(QueryRuleOptimizationClusterData.RankPath, false)], QueryRuleOptimizationClusterData.PageSize), AllowFullScan: consent);
    }
    internal static AstQueryRequest Builder(PartitionRef partition, int collection, int shape, bool reference)
    {
        var query = KeyLoadQuery.From<QueryRuleOptimizationClusterRow>(partition, QueryRuleOptimizationClusterData.Collections[collection], IntegrationClientOptions.Translation());
        query = shape switch
        {
            QueryRuleOptimizationClusterData.RevisionOneShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 1L),
            QueryRuleOptimizationClusterData.OpenRevisionOneShape => query.Where(row => row.Status == "open" && QueryFunctions.DocumentRevision(row) == 1L),
            QueryRuleOptimizationClusterData.OpenRevisionTwoShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 2L && row.Status == "open"),
            QueryRuleOptimizationClusterData.RawShadowShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 99L),
            _ => query.Where(row => row.Nested.Revision == 7m)
        };
        var request = query.OrderBy(row => row.Rank).Select(row => new
        {
            status = row.Status,
            rank = row.Rank
        })
            .Take(QueryRuleOptimizationClusterData.PageSize).ToRequest(true);
        return !reference ? request : request with
        {
            Query = request.Query with
            {
                Filter = new Logical(request.Query.Filter!, QueryRuleOptimizationClusterData.OrOperator, request.Query.Filter!)
            }
        };
    }
}
