using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;

namespace KeyLoad.IntegrationTests.RelationalStorage;

/// <summary>Owns actual seed, state and four-transport result oracles for public Q2 rejection workflows.</summary>
internal static class RelationalSqlClusterJoinRejectionFlow
{
    internal const int Q2 = 2;
    internal const string PrivateName = "public-join-rejection-private-name";
    internal const string Prefix = "SELECT l.key AS order_key, r.name AS customer_name, l.title AS customer_id FROM agentrows AS l ";
    internal const string Join = "INNER JOIN joincustomers AS r ON l.title = r.id ";
    internal const string Order = "ORDER BY l.key ASC LIMIT 10";
    private const string Healthy = Prefix + Join + Order;
    private const string Right = "joincustomers";
    private const string Id = "id";
    private const string Name = "name";
    private const string OrderId = "rejection-order";
    private const string CustomerId = "rejection-customer";
    private const string LeftJson = "{\"key\":\"rejection-order\",\"title\":\"rejection-customer\",\"count\":1}";
    private const string RightJson = "{\"id\":\"rejection-customer\",\"name\":\"public-join-rejection-private-name\"}";
    private const string OrderField = "order_key";
    private const string CustomerField = "customer_id";
    private const string NameField = "customer_name";
    private const string AccessPath = "bounded-primary-key-inner-join";
    private const int ProjectionCount = 3;

    internal static async Task<(RelationalSqlClusterScenario Scenario, long Position)> SeedAsync(KeyLoadClient sdk, CancellationToken cancellationToken)
    {
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, cancellationToken);
        var resource = new ResourceDefinition(Right, ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        {
            RelationalSchema = new(Id, [new(Id, RelationalColumnType.Text), new(Name, RelationalColumnType.Text)])
        };
        var configured = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(sdk,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, resource),
                Guid.NewGuid()), cancellationToken);
        await Assert.That(configured.RelationalSchema!.PrimaryKey).IsEqualTo(Id);
        var committed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Command(
            new PutDocument(RelationalSqlClusterTokens.Table, OrderId, LeftJson),
            new PutDocument(Right, CustomerId, RightJson)), cancellationToken));
        return (scenario, committed.Token.Position);
    }

    internal static async Task<(DocumentResult? Left, DocumentResult? Right)> ReadRowsAsync(KeyLoadClient sdk,
        RelationalSqlClusterScenario scenario, CancellationToken cancellationToken)
    {
        var left = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(
            new(scenario.Partition, RelationalSqlClusterTokens.Table, OrderId), cancellationToken));
        var right = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(
            new(scenario.Partition, Right, CustomerId), cancellationToken));
        await Assert.That(left).IsNotNull();
        await Assert.That(right).IsNotNull();
        var expectedLeft = new DocumentResult(new(scenario.Partition, RelationalSqlClusterTokens.Table, OrderId),
            1, LeftJson, false, []);
        var expectedRight = new DocumentResult(new(scenario.Partition, Right, CustomerId),
            1, RightJson, false, []);
        await SqlClusterProtocol.EqualAsync(expectedLeft, left!);
        await SqlClusterProtocol.EqualAsync(expectedRight, right!);
        return (left, right);
    }

    internal static async Task VerifyRowsAsync(KeyLoadClient sdk, RelationalSqlClusterScenario scenario,
        (DocumentResult? Left, DocumentResult? Right) before, CancellationToken cancellationToken)
    {
        var after = await ReadRowsAsync(sdk, scenario, cancellationToken);
        await SqlClusterProtocol.EqualAsync(before.Left, after.Left);
        await SqlClusterProtocol.EqualAsync(before.Right, after.Right);
    }

    internal static async Task<QueryPage> ReadHealthyAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        PartitionRef partition, CancellationToken cancellationToken)
    {
        var query = new QueryRequest(partition, Healthy, AllowFullScan: true, QueryDialectVersion: Q2);
        var sql = new SqlOperationRequest(partition, Healthy, AllowFullScan: true, QueryDialectVersion: Q2);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(query, cancellationToken));
        var unified = await SqlClusterProtocol.SdkAsync<QueryPage>(sdk, sql, cancellationToken);
        var officialQuery = (await McpCallerAssertions.SuccessAsync<QueryPage>(
            await mcp.CallAsync(McpCallerTools.QueryExecute, query, cancellationToken))).Value;
        var officialSql = await SqlClusterProtocol.McpAsync<QueryPage>(mcp, sql, cancellationToken);
        await Assert.That(direct.Rows).HasSingleItem();
        await Assert.That(direct.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(direct.Cursor).IsNull();
        await Assert.That(direct.Rows[0].EntityId).IsEqualTo(OrderId);
        await Assert.That(direct.Rows[0].Redacted).IsFalse();
        await Assert.That(direct.Rows[0].RedactedFields!.Value).IsEmpty();
        await RelationalSqlClusterJoinPageAssertions.RevisionsAsync(direct, 1, 1);
        using var json = JsonDocument.Parse(direct.Rows[0].Json);
        await Assert.That(json.RootElement.EnumerateObject().Count()).IsEqualTo(ProjectionCount);
        await Assert.That(json.RootElement.GetProperty(OrderField).GetString()).IsEqualTo(OrderId);
        await Assert.That(json.RootElement.GetProperty(CustomerField).GetString()).IsEqualTo(CustomerId);
        await Assert.That(json.RootElement.GetProperty(NameField).GetString()).IsEqualTo(PrivateName);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(direct, unified);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(direct, officialQuery);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(direct, officialSql);
        return direct;
    }
}
