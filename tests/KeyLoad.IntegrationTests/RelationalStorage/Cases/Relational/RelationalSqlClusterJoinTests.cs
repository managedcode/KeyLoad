using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.QueryExecution;
using TUnit.Assertions.Enums;

namespace KeyLoad.IntegrationTests.RelationalStorage;

/// <summary>Exercises the same bounded Q2 join through the actual Orleans SDK and official MCP clients.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlClusterJoinTests(ClusterFixture fixture)
{
    private const string RightCollection = "joincustomers";
    private const string RightId = "id";
    private const string RightName = "name";
    private const string CustomerNameField = "customer_name";
    private const string CustomerIdField = "customer_id";
    private const string AccessPath = "bounded-primary-key-inner-join";
    private const string QueryText = "SELECT l.key AS order_key, r.name AS customer_name, l.title AS customer_id "
        + "FROM agentrows AS l INNER JOIN joincustomers AS r ON l.title = r.id "
        + "ORDER BY l.key ASC OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY";
    private const int QueryDialectVersion = SqlOperationProtocol.DefaultQueryDialectVersion;

    [Test]
    public async Task Q2JoinReturnsTheSameCommittedPairsToSdkAndOfficialMcpAcrossCluster()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        var rightDefinition = new ResourceDefinition(RightCollection, ResourceKind.Collection,
            scenario.Partition.TransactionDomainId)
        {
            RelationalSchema = new(RightId,
                [new(RightId, RelationalColumnType.Text), new(RightName, RelationalColumnType.Text)])
        };
        var configured = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(sdk,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, rightDefinition),
                Guid.NewGuid()), deadline.Token);
        await Assert.That(configured.RelationalSchema!.PrimaryKey).IsEqualTo(RightId);
        var seedCommand = scenario.Command(
            new PutDocument(RelationalSqlClusterTokens.Table, "order-z", "{\"key\":\"order-z\",\"title\":\"cust-2\",\"count\":2}"),
            new PutDocument(RelationalSqlClusterTokens.Table, "order-a", "{\"key\":\"order-a\",\"title\":\"cust-1\",\"count\":1}"),
            new PutDocument(RightCollection, "cust-2", "{\"id\":\"cust-2\",\"name\":\"Lin\"}"),
            new PutDocument(RightCollection, "cust-1", "{\"id\":\"cust-1\",\"name\":\"Ada\"}"));
        var seed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(seedCommand, deadline.Token));
        var query = new QueryRequest(scenario.Partition, QueryText, AllowFullScan: true,
            QueryDialectVersion: QueryDialectVersion);
        var sql = new SqlOperationRequest(scenario.Partition, QueryText, AllowFullScan: true,
            QueryDialectVersion: QueryDialectVersion);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(query, deadline.Token));
        var unified = await SqlClusterProtocol.SdkAsync<QueryPage>(sdk, sql, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var officialQuery = (await McpCallerAssertions.SuccessAsync<QueryPage>(
            await mcp.CallAsync(McpCallerTools.QueryExecute, query, deadline.Token))).Value;
        var officialSql = await SqlClusterProtocol.McpAsync<QueryPage>(mcp, sql, deadline.Token);

        await Assert.That(direct.AccessPath).IsEqualTo(AccessPath);
        await Assert.That(direct.Rows.Select(row => row.EntityId)).IsEquivalentTo(["order-a", "order-z"], CollectionOrdering.Matching);
        await Assert.That(direct.CutPosition).IsGreaterThanOrEqualTo(seed.Token.Position);
        await RelationalSqlClusterJoinPageAssertions.EquivalentAsync(direct, unified);
        await RelationalSqlClusterJoinPageAssertions.EquivalentAsync(direct, officialQuery);
        await RelationalSqlClusterJoinPageAssertions.EquivalentAsync(direct, officialSql);
        await RelationalSqlClusterJoinPageAssertions.RevisionsAsync(direct, 1, 1);
        using var first = JsonDocument.Parse(direct.Rows[0].Json);
        await Assert.That(first.RootElement.GetProperty(CustomerNameField).GetString()).IsEqualTo("Ada");
        await Assert.That(first.RootElement.GetProperty(CustomerIdField).GetString()).IsEqualTo("cust-1");
    }
}
