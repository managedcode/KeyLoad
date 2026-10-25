using System.Linq.Expressions;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class QueryAdapterNullParityProtocol
{
    internal const string Collection = "orders";
    internal const string Root = "root";
    internal const string Reader = "adapter-null-reader";
    internal const string Sql = "SELECT * FROM orders WHERE status IN ('open','hold') AND n IS NULL ORDER BY id";
    internal static readonly Guid SeedId = new("9044c9f9-2584-4ba9-b7b5-d3877d4771bb");
    internal static Mutation[] Seed => [new PutDocument(Collection, "null", "{\"n\":null,\"status\":\"open\"}"),
        new PutDocument(Collection, "missing", "{\"status\":\"hold\"}"),
        new PutDocument(Collection, "value", "{\"n\":2,\"status\":\"closed\"}")];
    internal static AstQueryRequest Json(PartitionRef partition)
        => QueryAdapterTestSupport.RoundTrip(new(partition,
            new(Collection, null, [new("*", "*")],
                new Logical(new InPredicate(new FieldOperand("/status"),
                    [ValueOperand.Create("open"), ValueOperand.Create("hold")], false), "AND",
                    new NullTest(new FieldOperand("/n"), false, false)), [new("/@id", false)], 100),
            AllowFullScan: true));
    internal static AstQueryRequest CSharp(PartitionRef partition, bool includeMissing = false)
    {
        var set = new[] { "open", "hold" };
        Expression<Func<QueryAdapterOrder, bool>> filter = includeMissing
            ? row => set.Contains(row.Status) && (QueryFunctions.IsMissing(row.Optional) || QueryFunctions.IsNull(row.Optional))
            : row => set.Contains(row.Status) && QueryFunctions.IsNull(row.Optional);
        return QueryAdapterTestSupport.RoundTrip(KeyLoadQuery.From<QueryAdapterOrder>(partition, Collection,
            UnitClientOptions.Translation()).Where(filter).OrderBy(row => QueryFunctions.DocumentId(row)).ToRequest(true));
    }
    internal static QueryPage Expected(long position, bool includeMissing = false)
        => new(includeMissing
            ? [new("missing", 1, "{\"status\":\"hold\"}", false, []), new("null", 1, "{\"n\":null,\"status\":\"open\"}", false, [])]
            : [new("null", 1, "{\"n\":null,\"status\":\"open\"}", false, [])], null, position, "bounded-full-scan");
}
