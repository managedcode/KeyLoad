using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.QueryExecution;

internal static class QueryCapabilityVersionRequests
{
    private const string ActiveParameterName = "active";
    private const string MinimumParameterName = "minimum";
    internal const string Collection = "query_versions";
    internal const string Reader = "query-version-reader";
    internal static DatabaseLimits Limits() => new()
    {
    };
    internal static CommandRequest Seed(PartitionRef partition) => new(Guid.NewGuid(), partition,
        [new PutDocument(Collection, "a", """{"order_id":"a","title":"Alpha","score":10,"active":true}"""),
         new PutDocument(Collection, "b", """{"order_id":"b","title":"Bravo","score":20,"active":true}"""),
         new PutDocument(Collection, "c", """{"order_id":"c","title":"Charlie","score":30,"active":false}"""),
         new PutDocument(Collection, "d", """{"order_id":"d","title":"Delta","score":40,"active":true}""")]);
    internal static AstQueryRequest Builder(PartitionRef partition) => Query(partition).ToRequest(true);
    internal static AstQueryRequest Explain(PartitionRef partition) => Query(partition).Explain().ToRequest(true);
    private static KeyLoadQuery<QueryCapabilityVersionRow> Query(PartitionRef partition) => KeyLoadQuery.From<QueryCapabilityVersionRow>(partition, Collection, UnitClientOptions.Translation())
        .Where(row => row.Active == true && row.Score >= 20m).OrderBy(row => row.OrderId)
        .Select(row => new
        {
            title = row.Title,
            score = row.Score
        }).Take(1);
    internal static AstQueryRequest Ast(PartitionRef partition, int version) => new(partition,
        new(Collection, null, [new("/title", "title"), new("/score", "score")],
            new Logical(new Comparison(new FieldOperand("/active"), "=", ValueOperand.Create(true)), "AND",
                new Comparison(new FieldOperand("/score"), ">=", ValueOperand.Create(20m))), [new("/order_id", false)], 1), AllowFullScan: true, AstVersion: version);
    internal static QueryRequest Sql(PartitionRef partition, int version) => new(partition,
        "SELECT TOP (1) title,score FROM query_versions WHERE active = @active AND score >= @minimum ORDER BY order_id",
        new()
        {
            [ActiveParameterName] = JsonSerializer.SerializeToElement(true),
            [MinimumParameterName] = JsonSerializer.SerializeToElement(20m)
        },
        AllowFullScan: true, QueryDialectVersion: version);
}
