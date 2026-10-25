using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed record MetamorphicReferencePlan(QueryRequest Sql, AstQueryRequest Parsed, AstQueryRequest Json, AstQueryRequest CSharp)
{
    internal static MetamorphicReferencePlan Create(PartitionRef partition, int shape, bool descending, int offset)
    {
        var sql = MetamorphicReferenceRequests.Sql(partition, shape, descending, offset);
        var parsed = new AstQueryRequest(partition,
            new SqlParser(sql.Sql, UnitExecutionOptions.QueryExecution()).Parse(),
            sql.Parameters, AllowFullScan: true);
        parsed = OrleansSerialization.Deserialize<AstQueryRequest>(OrleansSerialization.Serialize(parsed));
        return new(sql, parsed, MetamorphicReferenceRequests.Json(partition, shape, descending, offset),
            MetamorphicReferenceRequests.CSharp(partition, shape, descending, offset));
    }
}
