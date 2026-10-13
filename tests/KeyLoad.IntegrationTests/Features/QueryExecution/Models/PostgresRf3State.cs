using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresRf3State(McpPersistedIdentity Writer, McpPersistedIdentity Reader,
    CommandRequest Seed, CommitReceipt Receipt, int Dialect)
{
    internal static PartitionRef Partition => PostgresRf3Protocol.Partition;
    internal string Sql => Dialect == PostgresRf3Protocol.JoinDialect
        ? RelationalSqlRf3JoinBudgetData.Sql : ScalarSql;
    internal QueryRequest Query => new(Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect);
    internal SqlOperationRequest Operation => new(Partition, Sql, AllowFullScan: true, QueryDialectVersion: Dialect);
    internal static readonly EntityRef Left = new(Partition, RelationalSqlRf3JoinBudgetData.LeftCollection, OrderId);
    internal static readonly EntityRef Right = new(Partition, RelationalSqlRf3JoinBudgetData.RightCollection, CustomerId);
    internal const string OrderId = "order-native";
    internal const string CustomerId = "customer-native";
    internal const string LeftJson = "{\"customer_id\":\"customer-native\",\"detail\":\"small\",\"order_id\":\"order-native\"}";
    internal const string RightJson = "{\"id\":\"customer-native\",\"name\":\"Ada\"}";
    internal const string ScalarSql = "SELECT * FROM budgetorders ORDER BY order_id ASC LIMIT 10";
    internal const string JoinedJson = "{\"order_id\":\"order-native\",\"left_detail\":\"small\",\"right_name\":\"Ada\",\"customer_id\":\"customer-native\"}";
}
