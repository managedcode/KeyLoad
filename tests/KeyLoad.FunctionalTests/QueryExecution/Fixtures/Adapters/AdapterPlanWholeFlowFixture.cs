using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.FunctionalTests.QueryExecution;

internal static class AdapterPlanWholeFlowFixture
{
    private const string StatusParameter = "status";
    private const string MinimumParameter = "minimum";
    private const string StatusPath = "/status";
    private const string NumberPath = "/number";
    private const string IdPath = "/@id";
    internal const string Sql = "SELECT TOP (10) d.id, d.number, d.status FROM orders d WHERE d.status = @status AND d.number >= @minimum ORDER BY d.id";
    internal static Dictionary<string, JsonElement> Parameters() => new(StringComparer.Ordinal)
    {
        [StatusParameter] = JsonSerializer.SerializeToElement("open"),
        [MinimumParameter] = JsonSerializer.SerializeToElement(2m)
    };
    internal static string ExpectedPlan(PartitionRef partition, long cut) => JsonSerializer.Serialize(new
    {
        AccessPath = "index:status",
        AtomicPartition = partition.AtomicPartitionId,
        ReadCut = cut,
        Scan = "index-prefix-seek",
        Index = StatusParameter,
        SeekFields = new[] { StatusPath },
        HasLowerBound = false,
        HasUpperBound = false,
        ResidualFields = new[] { StatusPath, NumberPath },
        Order = new Ordering[] { new(IdPath, false) },
        RetainedRows = 10L,
        Limit = 10,
        Offset = 0,
        Projection = new Selection[] { new(IdPath, "id"), new(NumberPath, "number"), new(StatusPath, StatusParameter) },
        Operators = new[] { "index-prefix-seek", "row-authorization", "residual-filter", "top-n-heap", "projection", "page" }
    }, JsonDefaults.Options);
    internal static AstQueryRequest Ast(TestDatabase database, bool explain)
    {
        var query = new SqlParser(Sql, UnitExecutionOptions.QueryExecution()).Parse() with
        {
            Explain = explain
        };
        return QueryAdapterTestSupport.RoundTrip(new(database.Partition, query, Parameters()));
    }
    internal static AstQueryRequest CSharp(TestDatabase database, bool explain)
    {
        var query = KeyLoadQuery.From<QueryAdapterOrder>(database.Partition, "orders", UnitClientOptions.Translation())
            .Where(row => row.Status == "open" && row.Number >= 2m).OrderBy(row => QueryFunctions.DocumentId(row))
            .Select(row => new { Id = QueryFunctions.DocumentId(row), row.Number, row.Status }).Take(10);
        return QueryAdapterTestSupport.RoundTrip((explain ? query.Explain() : query).ToRequest());
    }
    internal static void Seed(TestDatabase database)
    {
        database.Configure("orders", ResourceKind.Collection, indexes: [new("status", ["/status"])]);
        database.Commit(new PutDocument("orders", "a", "{\"number\":2,\"status\":\"open\"}"),
            new PutDocument("orders", "b", "{\"number\":1,\"status\":\"open\"}"),
            new PutDocument("orders", "c", "{\"number\":3,\"status\":\"hold\"}"));
    }
    internal static QueryPage[] Execute(TestDatabase database, QueryEngine engine, string principal, bool explain)
        => explain
            ? [engine.ExecuteAst(principal, Ast(database, true)), engine.ExecuteAst(principal, CSharp(database, true))]
            : [engine.Execute(principal, new(database.Partition, Sql, Parameters())),
               engine.ExecuteAst(principal, Ast(database, false)), engine.ExecuteAst(principal, CSharp(database, false))];
}
