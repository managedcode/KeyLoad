namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal static class LeftFilteredJoinProtocol
{
    private const string MinimumFieldKey = "minimum";
    internal const string Left = "filteredorders";
    internal const string Right = "filteredcustomers";
    internal const string UseGrant = "filter.use";
    internal const string FilterReadGrant = "filter.read";
    internal const string NameReadGrant = "name.read";
    internal const string Prefix = "SELECT l.order_id AS order_id, r.name AS customer_name, l.total AS total "
        + "FROM filteredorders AS l INNER JOIN filteredcustomers AS r ON l.customer_id = r.id ";
    internal const string Order = "ORDER BY l.order_id ASC LIMIT 10";
    internal const string Sql = Prefix + "WHERE l.total >= @minimum AND NOT (l.total IS NULL) " + Order;
    internal const string LeftA = "{\"order_id\":\"a\",\"customer_id\":\"c1\",\"total\":7}";
    internal const string LeftB = "{\"order_id\":\"b\",\"customer_id\":\"c2\",\"total\":2}";
    internal const string RightA = "{\"id\":\"c1\",\"name\":\"Ada\"}";
    internal const string RightB = "{\"id\":\"c2\",\"name\":\"Lin\"}";

    internal static Dictionary<string, System.Text.Json.JsonElement> Parameters()
        => new() { [MinimumFieldKey] = System.Text.Json.JsonSerializer.SerializeToElement(7) };
    internal static QueryRequest Query(PartitionRef partition)
        => new(partition, Sql, Parameters(), AllowFullScan: true, QueryDialectVersion: 2);
    internal static SqlOperationRequest Request(PartitionRef partition)
        => new(partition, Sql, Parameters(), AllowFullScan: true, QueryDialectVersion: 2);
}
