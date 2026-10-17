using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class AdapterPlanWholeFlowFixture
{
    private const string StatusParameter = "status";
    private const string MinimumParameter = "minimum";
    internal const string Sql = "SELECT TOP (10) d.id, d.number, d.status FROM orders d WHERE d.status = @status AND d.number >= @minimum ORDER BY d.id";
    internal static Dictionary<string, JsonElement> Parameters() => new(StringComparer.Ordinal)
    {
        [StatusParameter] = JsonSerializer.SerializeToElement("open"),
        [MinimumParameter] = JsonSerializer.SerializeToElement(2m)
    };
    internal static AstQueryRequest Ast(TestDatabase database, bool explain)
    {
        var query = new SqlParser(Sql,
            UnitExecutionOptions.DatabaseLimits(new()), UnitExecutionOptions.QueryExecution()).Parse() with
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
