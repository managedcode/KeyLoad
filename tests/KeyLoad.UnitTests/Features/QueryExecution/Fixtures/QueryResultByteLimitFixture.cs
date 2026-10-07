using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class QueryResultByteLimitFixture
{
    internal const string Root = "root";
    internal const string Left = "resultorders";
    internal const string Right = "resultcustomers";
    private const string IdColumn = "id";
    internal const int ResultCap = 4_096;
    internal const int NativeCap = 8_192;
    internal const int LargerCap = 16_384;

    internal static void Seed(TestDatabase database, int payloadCharacters)
    {
        Configure(database, Left, new(IdColumn, [new(IdColumn, RelationalColumnType.Text),
            new("customer_id", RelationalColumnType.Text), new("payload", RelationalColumnType.Text)]));
        Configure(database, Right, new(IdColumn, [new(IdColumn, RelationalColumnType.Text), new("payload", RelationalColumnType.Text)]));
        foreach (var suffix in new[] { "a", "b" })
        {
            var id = "order-" + suffix;
            var customer = "customer-" + suffix;
            var payload = new string('x', payloadCharacters);
            database.Commit(new PutDocument(Left, id, JsonSerializer.Serialize(new { id, customer_id = customer, payload })));
            database.Commit(new PutDocument(Right, customer, JsonSerializer.Serialize(new { id = customer, payload })));
        }
    }

    internal static AstQueryRequest Ast(TestDatabase database, bool join, bool compact)
    {
        var projection = compact ? new Selection("/id", "key", join ? "l" : null)
            : new Selection("/payload", "payload", join ? "l" : null);
        return new(database.Partition, new SelectQuery(Left, join ? "l" : null,
            join && !compact ? [projection, new("/payload", "right_payload", "r")] : [projection], null,
            [new("/id", false)], 2, InnerJoin: join ? new(Right, "r", "/customer_id", "/id") : null),
            AllowFullScan: true, AstVersion: join ? 2 : 1);
    }

    internal static QueryRequest Sql(TestDatabase database, bool join, bool compact)
    {
        var columns = compact ? (join ? "l.id AS key" : "id AS key")
            : (join ? "l.payload AS payload, r.payload AS right_payload" : "payload AS payload");
        var from = join ? Left + " AS l INNER JOIN " + Right + " AS r ON l.customer_id = r.id" : Left;
        return new(database.Partition, "SELECT " + columns + " FROM " + from + " ORDER BY "
            + (join ? "l.id" : "id") + " ASC LIMIT 2", AllowFullScan: true, QueryDialectVersion: join ? 2 : 1);
    }

    internal static byte[] Sources(TestDatabase database)
        => JsonDefaults.Serialize(new[] { (Left, "order-a"), (Left, "order-b"), (Right, "customer-a"), (Right, "customer-b") }
            .Select(pair => database.Database.GetDocument(Root, new(database.Partition, pair.Item1, pair.Item2))).ToArray());

    internal static QueryPage Execute(QueryEngine engine, TestDatabase database, bool ast, bool join, bool compact)
        => ast ? engine.ExecuteAst(Root, Ast(database, join, compact), cancellationToken: TestContext.Current!.Execution.CancellationToken)
            : engine.Execute(Root, Sql(database, join, compact), cancellationToken: TestContext.Current!.Execution.CancellationToken);

    private static void Configure(TestDatabase database, string name, RelationalSchema schema)
        => database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, new ResourceDefinition(name, ResourceKind.Collection, database.Partition.TransactionDomainId)
            { RelationalSchema = schema })).Get<ResourceDefinition>();
}
