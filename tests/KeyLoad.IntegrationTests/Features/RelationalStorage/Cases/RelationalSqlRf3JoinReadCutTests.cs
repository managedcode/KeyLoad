using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Checks RF3 joined pairs while real atomic commands change both linked rows.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlRf3JoinReadCutTests(ClusterFixture fixture)
{
    private const string RightCollection = "joincustomers";
    private const string RightId = "id";
    private const string RightName = "name";
    private const string RightGeneration = "generation";
    private const string CustomerA = "join-customer-a";
    private const string CustomerB = "join-customer-b";
    private const string OrderId = "join-order";
    private const string CustomerName = "joined-customer";
    private const string CustomerIdField = "customer_id";
    private const string LeftGenerationField = "left_generation";
    private const string RightGenerationField = "right_generation";
    private const string QueryText = "SELECT l.key AS order_key, r.name AS customer_name, l.title AS customer_id, "
        + "l.count AS left_generation, r.generation AS right_generation FROM agentrows AS l "
        + "INNER JOIN joincustomers AS r ON l.title = r.id ORDER BY l.key ASC LIMIT 10";
    private const int QueryDialectVersion = 2;
    private const int FirstReaderRoute = 0;
    private const int ReaderRouteCount = 4;
    private const int SdkSqlRoute = 1;
    private const int McpQueryRoute = 2;
    private const int McpSqlRoute = 3;
    private const int FirstGeneration = 0;
    private const int FirstTransition = 1;
    private const int FirstLeftRevision = 1;
    private const int FirstRightRevision = 1;
    private const int TransitionCount = 6;

    [Test]
    public async Task ConcurrentAtomicPairChangesNeverExposeAMixedRf3Generation()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        await ConfigureRightAsync(sdk, scenario, deadline.Token);
        var seed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Command(
            PutLeft(CustomerA, FirstGeneration), PutRight(CustomerA, FirstGeneration), PutRight(CustomerB, FirstGeneration)), deadline.Token));
        var query = new QueryRequest(scenario.Partition, QueryText, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion);
        var sql = new SqlOperationRequest(scenario.Partition, QueryText, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        var previousCustomer = CustomerA;
        var previousGeneration = FirstGeneration;
        var leftRevision = FirstLeftRevision;
        var customerARevision = FirstRightRevision;
        var customerBRevision = FirstRightRevision;
        for (var transition = FirstTransition; transition <= TransitionCount; transition++)
        {
            var nextCustomer = transition % 2 == 0 ? CustomerA : CustomerB;
            var nextGeneration = transition;
            var previousRightRevision = previousCustomer == CustomerA ? customerARevision : customerBRevision;
            var nextRightRevision = nextCustomer == CustomerA ? customerARevision : customerBRevision;
            var queryTask = StartQueryAsync((transition - 1) % ReaderRouteCount, sdk, mcp, query, sql, deadline.Token);
            var updateTask = sdk.CommitAsync(scenario.Command(PutLeft(nextCustomer, nextGeneration, leftRevision),
                PutRight(nextCustomer, nextGeneration, nextRightRevision)), deadline.Token);
            await Task.WhenAll(queryTask, updateTask);
            var page = await queryTask;
            var receipt = await McpCallerAssertions.SdkSuccessAsync(await updateTask);
            await Assert.That(receipt.Token.Position).IsGreaterThanOrEqualTo(seed.Token.Position);
            await AssertPairAsync(page, previousCustomer, previousGeneration, leftRevision, previousRightRevision,
                nextCustomer, nextGeneration, leftRevision + 1, nextRightRevision + 1, seed.Token.Position);
            previousCustomer = nextCustomer;
            previousGeneration = nextGeneration;
            leftRevision++;
            if (nextCustomer == CustomerA)
            {
                customerARevision++;
            }
            else
            {
                customerBRevision++;
            }
        }

        var finalPages = await ReadPagesAsync(sdk, mcp, query, sql, deadline.Token);
        var finalRightRevision = previousCustomer == CustomerA ? customerARevision : customerBRevision;
        await AssertCurrentPairAsync(finalPages.Direct, previousCustomer, previousGeneration, leftRevision, finalRightRevision, seed.Token.Position);
        await AssertCurrentPairAsync(finalPages.Unified, previousCustomer, previousGeneration, leftRevision, finalRightRevision, seed.Token.Position);
        await AssertCurrentPairAsync(finalPages.McpDirect, previousCustomer, previousGeneration, leftRevision, finalRightRevision, seed.Token.Position);
        await AssertCurrentPairAsync(finalPages.McpUnified, previousCustomer, previousGeneration, leftRevision, finalRightRevision, seed.Token.Position);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(finalPages.Direct, finalPages.Unified);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(finalPages.Direct, finalPages.McpDirect);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(finalPages.Direct, finalPages.McpUnified);
    }

    private static async Task ConfigureRightAsync(KeyLoadClient sdk, RelationalSqlRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var right = new ResourceDefinition(RightCollection, ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        {
            RelationalSchema = new(RightId, [new(RightId, RelationalColumnType.Text), new(RightName, RelationalColumnType.Text),
                new(RightGeneration, RelationalColumnType.WholeNumber)])
        };
        var configured = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(sdk,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, right)), cancellationToken);
        await Assert.That(configured.RelationalSchema!.PrimaryKey).IsEqualTo(RightId);
    }

    private static PutDocument PutLeft(string customer, int generation, long? expectedRevision = null)
        => new(RelationalSqlRf3Tokens.Table, OrderId, JsonSerializer.Serialize(
            new { key = OrderId, title = customer, count = generation }, JsonDefaults.Options), expectedRevision);

    private static PutDocument PutRight(string customer, int generation, long? expectedRevision = null)
        => new(RightCollection, customer, JsonSerializer.Serialize(
            new { id = customer, name = CustomerName, generation }, JsonDefaults.Options), expectedRevision);

    private static Task<QueryPage> StartQueryAsync(int route, KeyLoadClient sdk, McpOfficialClient mcp,
        QueryRequest query, SqlOperationRequest sql, CancellationToken cancellationToken) => route switch
        {
            FirstReaderRoute => ReadSdkAsync(sdk, query, cancellationToken),
            SdkSqlRoute => SqlRf3Protocol.SdkAsync<QueryPage>(sdk, sql, cancellationToken),
            McpQueryRoute => ReadMcpAsync(mcp, query, cancellationToken),
            McpSqlRoute => SqlRf3Protocol.McpAsync<QueryPage>(mcp, sql, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(route))
        };

    private static async Task<QueryPage> ReadSdkAsync(KeyLoadClient sdk, QueryRequest query,
        CancellationToken cancellationToken)
        => await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(query, cancellationToken));

    private static async Task<QueryPage> ReadMcpAsync(McpOfficialClient mcp, QueryRequest query,
        CancellationToken cancellationToken)
        => (await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, query, cancellationToken))).Value;

    private static async Task<(QueryPage Direct, QueryPage Unified, QueryPage McpDirect, QueryPage McpUnified)>
        ReadPagesAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueryRequest query, SqlOperationRequest sql,
            CancellationToken cancellationToken)
    {
        var direct = await ReadSdkAsync(sdk, query, cancellationToken);
        var unified = await SqlRf3Protocol.SdkAsync<QueryPage>(sdk, sql, cancellationToken);
        var mcpDirect = await ReadMcpAsync(mcp, query, cancellationToken);
        var mcpUnified = await SqlRf3Protocol.McpAsync<QueryPage>(mcp, sql, cancellationToken);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(direct);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(unified);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(mcpDirect);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(mcpUnified);
        return (direct, unified, mcpDirect, mcpUnified);
    }

    private static async Task AssertPairAsync(QueryPage page, string previousCustomer, int previousGeneration,
        long previousLeftRevision, long previousRightRevision, string nextCustomer, int nextGeneration,
        long nextLeftRevision, long nextRightRevision, long minimumCut)
    {
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimumCut);
        await Assert.That(page.Rows).HasSingleItem();
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(page);
        using var json = JsonDocument.Parse(page.Rows[0].Json);
        var customer = json.RootElement.GetProperty(CustomerIdField).GetString();
        var leftGeneration = json.RootElement.GetProperty(LeftGenerationField).GetInt32();
        var rightGeneration = json.RootElement.GetProperty(RightGenerationField).GetInt32();
        await Assert.That(leftGeneration).IsEqualTo(rightGeneration);
        var sources = page.Rows[0].Sources!.Value;
        var isPrevious = customer == previousCustomer && leftGeneration == previousGeneration
            && sources[0].Revision == previousLeftRevision && sources[1].Revision == previousRightRevision;
        var isNext = customer == nextCustomer && leftGeneration == nextGeneration
            && sources[0].Revision == nextLeftRevision && sources[1].Revision == nextRightRevision;
        await Assert.That(isPrevious || isNext).IsTrue();
    }

    private static async Task AssertCurrentPairAsync(QueryPage page, string customer, int generation,
        long leftRevision, long rightRevision, long minimumCut)
    {
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimumCut);
        await Assert.That(page.Rows).HasSingleItem();
        await RelationalSqlRf3JoinPageAssertions.RevisionsAsync(page, leftRevision, rightRevision);
        using var json = JsonDocument.Parse(page.Rows[0].Json);
        await Assert.That(json.RootElement.GetProperty(CustomerIdField).GetString()).IsEqualTo(customer);
        await Assert.That(json.RootElement.GetProperty(LeftGenerationField).GetInt32()).IsEqualTo(generation);
        await Assert.That(json.RootElement.GetProperty(RightGenerationField).GetInt32()).IsEqualTo(generation);
    }
}
