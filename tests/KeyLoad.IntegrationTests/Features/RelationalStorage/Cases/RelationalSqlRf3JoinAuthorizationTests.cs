using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Proves both RF3 callers fail closed for persisted right-side scope and join-key-use denial.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlRf3JoinAuthorizationTests(ClusterFixture fixture)
{
    private const string RightCollection = "joincustomers";
    private const string RightId = "id";
    private const string RightName = "name";
    private const string PrivateCustomer = "join-private-customer-value";
    private const string RightUseGrant = "join.customer-id.use";
    private const string RightFieldPath = "/id";
    private const string CustomerNameField = "customer_name";
    private const string CustomerIdField = "customer_id";
    private const string QueryText = "SELECT l.key AS order_key, r.name AS customer_name, l.title AS customer_id "
        + "FROM agentrows AS l INNER JOIN joincustomers AS r ON l.title = r.id ORDER BY l.key ASC LIMIT 10";
    private const int QueryDialectVersion = 2;
    private const long InitialPolicyEpoch = 1;

    [Test]
    public async Task RightResourceAndJoinFieldUseDenialsHaveNoEffectsAndHealthyCallersStillRead()
    {
        using var deadline = McpCallerDeadline.Create();
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(admin, deadline.Token);
        var right = new ResourceDefinition(RightCollection, ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        {
            RelationalSchema = new(RightId, [new(RightId, RelationalColumnType.Text), new(RightName, RelationalColumnType.Text)]),
            FieldPolicies = [new(RightFieldPath, "join-private", RawUseGrant: RightUseGrant)]
        };
        await ConfigureRightAsync(admin, scenario, right, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(scenario.Command(
            new PutDocument(RelationalSqlRf3Tokens.Table, "join-order", "{\"key\":\"join-order\",\"title\":\"join-customer\",\"count\":1}"),
            new PutDocument(RightCollection, "join-customer", "{\"id\":\"join-customer\",\"name\":\"join-private-customer-value\"}")), deadline.Token));
        var limited = await CreateIdentityAsync(fixture, admin, scenario, Capability.DocumentsRead, deadline.Token);
        var missingUse = await CreateIdentityAsync(fixture, admin, scenario,
            Capability.Query | Capability.DocumentsRead, deadline.Token);
        using var limitedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        using var missingUseHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var limitedSdk = new KeyLoadClient(limitedHttp, limited.Secret, IntegrationClientOptions.Execution());
        var missingUseSdk = new KeyLoadClient(missingUseHttp, missingUse.Secret, IntegrationClientOptions.Execution());
        await using var limitedMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, limited.Secret, deadline.Token);
        await using var missingUseMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, missingUse.Secret, deadline.Token);
        var query = new QueryRequest(scenario.Partition, QueryText, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion);
        var sql = new SqlOperationRequest(scenario.Partition, QueryText, AllowFullScan: true, QueryDialectVersion: QueryDialectVersion);
        var beforeRows = await ReadRowsAsync(admin, scenario, deadline.Token);
        var before = await ReadAdministratorPagesAsync(fixture, admin, query, sql, deadline.Token);
        await Assert.That(before.Direct.Rows).HasSingleItem();
        await RelationalSqlRf3JoinPageAssertions.RevisionsAsync(before.Direct, 1, 1);
        using (var healthyJson = System.Text.Json.JsonDocument.Parse(before.Direct.Rows[0].Json))
        {
            await Assert.That(healthyJson.RootElement.GetProperty(CustomerNameField).GetString()).IsEqualTo(PrivateCustomer);
            await Assert.That(healthyJson.RootElement.GetProperty(CustomerIdField).GetString()).IsEqualTo("join-customer");
        }
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(before.Direct, before.Unified);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(before.Direct, before.McpDirect);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(before.Direct, before.McpUnified);
        await AssertDeniedAsync(limitedSdk, limitedMcp, query, sql, limited.Secret, deadline.Token);
        await AssertDeniedAsync(missingUseSdk, missingUseMcp, query, sql, missingUse.Secret, deadline.Token);
        var after = await ReadAdministratorPagesAsync(fixture, admin, query, sql, deadline.Token);
        var afterRows = await ReadRowsAsync(admin, scenario, deadline.Token);
        await SqlRf3Protocol.EqualAsync(beforeRows.Left, afterRows.Left);
        await SqlRf3Protocol.EqualAsync(beforeRows.Right, afterRows.Right);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(before.Direct, after.Direct);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(before.Unified, after.Unified);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(before.McpDirect, after.McpDirect);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(before.McpUnified, after.McpUnified);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(after.Direct, after.Unified);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(after.Direct, after.McpDirect);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(after.Direct, after.McpUnified);
    }

    private static async Task<(DocumentResult? Left, DocumentResult? Right)> ReadRowsAsync(KeyLoadClient admin,
        RelationalSqlRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var left = await McpCallerAssertions.SdkSuccessAsync(await admin.GetAsync(
            new(scenario.Partition, RelationalSqlRf3Tokens.Table, "join-order"), cancellationToken));
        var right = await McpCallerAssertions.SdkSuccessAsync(await admin.GetAsync(
            new(scenario.Partition, RightCollection, "join-customer"), cancellationToken));
        return (left, right);
    }

    private static async Task ConfigureRightAsync(KeyLoadClient admin, RelationalSqlRf3Scenario scenario,
        ResourceDefinition right, CancellationToken cancellationToken)
    {
        var configured = await SqlRf3Protocol.SdkAsync<ResourceDefinition>(admin,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, right)), cancellationToken);
        await Assert.That(configured.RelationalSchema!.PrimaryKey).IsEqualTo(RightId);
    }

    private static async Task<McpPersistedIdentity> CreateIdentityAsync(ClusterFixture fixture, KeyLoadClient admin,
        RelationalSqlRf3Scenario scenario, Capability rightCapabilities, CancellationToken cancellationToken)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            RelationalSqlRf3Tokens.Table, Capability.Query | Capability.DocumentsRead, cancellationToken);
        var principal = identity.Principal with
        {
            Grants = [new(scenario.Partition.DatabaseId, RelationalSqlRf3Tokens.Table, Capability.Query | Capability.DocumentsRead),
                new(scenario.Partition.DatabaseId, RightCollection, rightCapabilities)],
            PolicyEpoch = InitialPolicyEpoch
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), principal, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        return identity with { Principal = persisted };
    }

    private static async Task AssertDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueryRequest query,
        SqlOperationRequest sql, string secret, CancellationToken cancellationToken)
    {
        await Assert.That((await sdk.QueryAsync(query, cancellationToken)).Problem?.ErrorCode)
            .IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That((await sdk.ExecuteSqlAsync(sql, cancellationToken)).Problem?.ErrorCode)
            .IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var queryError = await mcp.CallAsync(McpCallerTools.QueryExecute, query, cancellationToken);
        await McpCallerAssertions.ErrorAsync(queryError, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(queryError, secret, PrivateCustomer);
        var sqlError = await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, cancellationToken);
        await McpCallerAssertions.ErrorAsync(sqlError, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(sqlError, secret, PrivateCustomer);
    }

    private static async Task<QueryPage> ReadMcpAsync(McpOfficialClient mcp, QueryRequest query,
        CancellationToken cancellationToken)
        => (await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, query, cancellationToken))).Value;

    private static async Task<(QueryPage Direct, QueryPage Unified, QueryPage McpDirect, QueryPage McpUnified)>
        ReadAdministratorPagesAsync(ClusterFixture fixture, KeyLoadClient admin, QueryRequest query,
            SqlOperationRequest sql, CancellationToken cancellationToken)
    {
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, cancellationToken);
        var direct = await McpCallerAssertions.SdkSuccessAsync(await admin.QueryAsync(query, cancellationToken));
        var unified = await SqlRf3Protocol.SdkAsync<QueryPage>(admin, sql, cancellationToken);
        var mcpDirect = await ReadMcpAsync(mcp, query, cancellationToken);
        var mcpUnified = await SqlRf3Protocol.McpAsync<QueryPage>(mcp, sql, cancellationToken);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(direct);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(unified);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(mcpDirect);
        await RelationalSqlRf3JoinPageAssertions.VerifySourcesAsync(mcpUnified);
        return (direct, unified, mcpDirect, mcpUnified);
    }
}
