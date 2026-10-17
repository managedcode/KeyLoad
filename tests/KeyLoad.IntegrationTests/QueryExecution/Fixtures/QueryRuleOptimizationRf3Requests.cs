using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueryRuleOptimizationRf3Requests
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
            QueryRuleOptimizationRf3Data.RevisionOneShape => RevisionOneSql,
            QueryRuleOptimizationRf3Data.OpenRevisionOneShape => OpenRevisionOneSql,
            QueryRuleOptimizationRf3Data.OpenRevisionTwoShape => OpenRevisionTwoSql,
            QueryRuleOptimizationRf3Data.RawShadowShape => RawShadowSql,
            _ => NestedSql
        };
        if (reference)
        {
            predicate = "(" + predicate + OrSqlSeparator + predicate + ")";
        }
        return new(partition, SelectSqlPrefix + QueryRuleOptimizationRf3Data.Collections[collection]
            + WhereSql + predicate + OrderSql, AllowFullScan: consent);
    }
    internal static AstQueryRequest Ast(PartitionRef partition, int collection, int shape, bool reference, bool consent = true)
    {
        Predicate revision = new Comparison(new FieldOperand(QueryRuleOptimizationRf3Data.RevisionPath), QueryRuleOptimizationRf3Data.EqualsOperator,
            ValueOperand.Create(shape == QueryRuleOptimizationRf3Data.OpenRevisionTwoShape ? 2m : shape == QueryRuleOptimizationRf3Data.RawShadowShape ? 99m : 1m));
        Predicate status = new Comparison(new FieldOperand(QueryRuleOptimizationRf3Data.StatusPath), QueryRuleOptimizationRf3Data.EqualsOperator, ValueOperand.Create("open"));
        var filter = shape switch
        {
            QueryRuleOptimizationRf3Data.OpenRevisionOneShape => new Logical(status, QueryRuleOptimizationRf3Data.AndOperator, revision),
            QueryRuleOptimizationRf3Data.OpenRevisionTwoShape => new Logical(revision, QueryRuleOptimizationRf3Data.AndOperator, status),
            QueryRuleOptimizationRf3Data.NestedShape => (Predicate)new Comparison(new FieldOperand(QueryRuleOptimizationRf3Data.NestedPath), QueryRuleOptimizationRf3Data.EqualsOperator, ValueOperand.Create(7m)),
            _ => revision
        };
        return new(partition, new(QueryRuleOptimizationRf3Data.Collections[collection], null,
            [new(QueryRuleOptimizationRf3Data.StatusPath, "status"), new(QueryRuleOptimizationRf3Data.RankPath, "rank")],
            reference ? new Logical(filter, QueryRuleOptimizationRf3Data.OrOperator, filter) : filter, [new(QueryRuleOptimizationRf3Data.RankPath, false)], QueryRuleOptimizationRf3Data.PageSize), AllowFullScan: consent);
    }
    internal static AstQueryRequest Builder(PartitionRef partition, int collection, int shape, bool reference)
    {
        var query = KeyLoadQuery.From<QueryRuleOptimizationRf3Row>(partition, QueryRuleOptimizationRf3Data.Collections[collection], IntegrationClientOptions.Translation());
        query = shape switch
        {
            QueryRuleOptimizationRf3Data.RevisionOneShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 1L),
            QueryRuleOptimizationRf3Data.OpenRevisionOneShape => query.Where(row => row.Status == "open" && QueryFunctions.DocumentRevision(row) == 1L),
            QueryRuleOptimizationRf3Data.OpenRevisionTwoShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 2L && row.Status == "open"),
            QueryRuleOptimizationRf3Data.RawShadowShape => query.Where(row => QueryFunctions.DocumentRevision(row) == 99L),
            _ => query.Where(row => row.Nested.Revision == 7m)
        };
        var request = query.OrderBy(row => row.Rank).Select(row => new
        {
            status = row.Status,
            rank = row.Rank
        })
            .Take(QueryRuleOptimizationRf3Data.PageSize).ToRequest(true);
        return !reference ? request : request with
        {
            Query = request.Query with
            {
                Filter = new Logical(request.Query.Filter!, QueryRuleOptimizationRf3Data.OrOperator, request.Query.Filter!)
            }
        };
    }
}
