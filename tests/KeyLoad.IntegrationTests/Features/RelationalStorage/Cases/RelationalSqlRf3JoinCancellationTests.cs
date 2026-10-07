using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Checks caller cancellation and a healthy joined read through actual RF3 clients.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlRf3JoinCancellationTests(ClusterFixture fixture)
{
    private const string RightCollection = "joincustomers";
    private const string RightId = "id";
    private const string RightName = "name";
    private const string QueryText = "SELECT l.key AS order_key, r.name AS customer_name, l.title AS customer_id "
        + "FROM agentrows AS l INNER JOIN joincustomers AS r ON l.title = r.id "
        + "ORDER BY l.key ASC LIMIT 10";
    private const int QueryDialectVersion = 2;
    private const string OrderId = "cancelled-join-order";
    private const string CustomerId = "cancelled-join-customer";

    [Test]
    public async Task CallerCancellationOnSdkAndOfficialMcpJoinPathsIsFollowedByHealthyReads()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        await ConfigureRightAsync(sdk, scenario, deadline.Token);
        var seed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Command(
            new PutDocument(RelationalSqlRf3Tokens.Table, OrderId, "{\"key\":\"cancelled-join-order\",\"title\":\"cancelled-join-customer\",\"count\":1}"),
            new PutDocument(RightCollection, CustomerId, "{\"id\":\"cancelled-join-customer\",\"name\":\"Ada\"}")), deadline.Token));
        var query = new QueryRequest(scenario.Partition, QueryText, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion);
        var sql = new SqlOperationRequest(scenario.Partition, QueryText, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await cancellation.CancelAsync();

        var sdkDirectFailure = await sdk.QueryAsync(query, cancellation.Token);
        await AssertCancelledAsync(sdkDirectFailure.IsSuccess, sdkDirectFailure.Problem?.ErrorCode);
        await AssertJoinedPageAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(query, deadline.Token)), seed.Token.Position);
        var sdkSqlFailure = await sdk.ExecuteSqlAsync(sql, cancellation.Token);
        await AssertCancelledAsync(sdkSqlFailure.IsSuccess, sdkSqlFailure.Problem?.ErrorCode);
        await AssertJoinedPageAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(sdk, sql, deadline.Token), seed.Token.Position);

        await Assert.ThrowsAsync<OperationCanceledException>(() => mcp.CallAsync(McpCallerTools.QueryExecute,
            query, cancellation.Token));
        await AssertJoinedPageAsync((await McpCallerAssertions.SuccessAsync<QueryPage>(
            await mcp.CallAsync(McpCallerTools.QueryExecute, query, deadline.Token))).Value, seed.Token.Position);
        await Assert.ThrowsAsync<OperationCanceledException>(() => mcp.CallAsync(SqlOperationProtocol.ToolName,
            sql, cancellation.Token));
        await AssertJoinedPageAsync(await SqlRf3Protocol.McpAsync<QueryPage>(mcp, sql, deadline.Token), seed.Token.Position);
    }

    private static async Task ConfigureRightAsync(KeyLoadClient sdk, RelationalSqlRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var right = new ResourceDefinition(RightCollection, ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        {
            RelationalSchema = new(RightId, [new(RightId, RelationalColumnType.Text), new(RightName, RelationalColumnType.Text)])
        };
        var configured = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(sdk,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, right)), cancellationToken);
        await Assert.That(configured.RelationalSchema!.PrimaryKey).IsEqualTo(RightId);
    }

    private static async Task AssertJoinedPageAsync(QueryPage page, long minimumCut)
    {
        await Assert.That(page.Rows).HasSingleItem();
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimumCut);
        await Assert.That(page.Rows[0].EntityId).IsEqualTo(OrderId);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(page);
    }

    private static async Task AssertCancelledAsync(bool isSuccess, string? errorCode)
    {
        await Assert.That(isSuccess).IsFalse();
        await Assert.That(errorCode).IsEqualTo(nameof(ErrorCode.Cancelled));
    }
}
