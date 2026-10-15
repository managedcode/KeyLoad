using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record MetamorphicPublicPlan(QueryRequest Sql, AstQueryRequest Parsed, AstQueryRequest Json, AstQueryRequest CSharp)
{
    internal static MetamorphicPublicPlan Create(PartitionRef partition, int shape, bool descending, int offset)
    {
        var sql = MetamorphicPublicRequests.Sql(partition, shape, descending, offset);
        var parsed = new AstQueryRequest(partition,
            new SqlParser(sql.Sql, Options.Create(new DatabaseLimits()), Options.Create(new QueryExecutionOptions())).Parse(),
            sql.Parameters, AllowFullScan: true);
        parsed = NativeSerialization.Deserialize<AstQueryRequest>(NativeSerialization.Serialize(parsed));
        return new(sql, parsed, MetamorphicPublicRequests.Json(partition, shape, descending, offset),
            MetamorphicPublicRequests.CSharp(partition, shape, descending, offset));
    }
}
