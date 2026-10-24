using KeyLoad.IntegrationTests.RelationalStorage;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class PostgresClusterState
{
    internal static PartitionRef Partition => PostgresClusterProtocol.Partition;
    internal static readonly EntityRef Left = new(Partition, RelationalSqlClusterJoinBudgetData.LeftCollection, OrderId);
    internal static readonly EntityRef Right = new(Partition, RelationalSqlClusterJoinBudgetData.RightCollection, CustomerId);
    internal const string OrderId = "order-execution";
    internal const string CustomerId = "customer-execution";
    internal const string LeftJson = "{\"customer_id\":\"customer-execution\",\"detail\":\"small\",\"order_id\":\"order-execution\"}";
    internal const string RightJson = "{\"id\":\"customer-execution\",\"name\":\"Ada\"}";
    internal const string ScalarSql = "SELECT TOP (10) * FROM budgetorders ORDER BY order_id ASC";
    internal const string JoinedJson = "{\"order_id\":\"order-execution\",\"left_detail\":\"small\",\"right_name\":\"Ada\",\"customer_id\":\"customer-execution\"}";
}
