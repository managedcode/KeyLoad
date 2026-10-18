using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresClusterState
{
    internal static PartitionRef Partition => PostgresClusterProtocol.Partition;
    internal static readonly EntityRef Left = new(Partition, RelationalSqlClusterJoinBudgetData.LeftCollection, OrderId);
    internal static readonly EntityRef Right = new(Partition, RelationalSqlClusterJoinBudgetData.RightCollection, CustomerId);
    internal const string OrderId = "order-native";
    internal const string CustomerId = "customer-native";
    internal const string LeftJson = "{\"customer_id\":\"customer-native\",\"detail\":\"small\",\"order_id\":\"order-native\"}";
    internal const string RightJson = "{\"id\":\"customer-native\",\"name\":\"Ada\"}";
    internal const string ScalarSql = "SELECT TOP (10) * FROM budgetorders ORDER BY order_id ASC";
    internal const string JoinedJson = "{\"order_id\":\"order-native\",\"left_detail\":\"small\",\"right_name\":\"Ada\",\"customer_id\":\"customer-native\"}";
}
