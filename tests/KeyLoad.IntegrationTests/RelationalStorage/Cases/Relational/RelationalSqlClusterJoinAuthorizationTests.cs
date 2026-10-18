using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>Proves both CLUSTER callers fail closed for persisted right-side scope and join-key-use denial.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlClusterJoinAuthorizationTests(ClusterFixture fixture)
{
    private const string RightCollection = "joincustomers";
    private const string RightId = "id";
    private const string RightName = "name";
    private const string PrivateCustomer = "join-private-customer-value";
    private const string RightUseGrant = "join.customer-id.use";
    private const string RightFieldPath = "/id";
    private const string CustomerNameField = "customer_name";
    private const string CustomerIdField = "customer_id";
    private const string QueryText = "SELECT TOP (10) l.key AS order_key, r.name AS customer_name, l.title AS customer_id "
        + "FROM agentrows AS l INNER JOIN joincustomers AS r ON l.title = r.id ORDER BY l.key ASC";
    private const int QueryDialectVersion = 2;
    private const long InitialPolicyEpoch = 1;
    private const long UpdatedPolicyEpoch = 2;

    [Test]
    public async Task RightResourceAndJoinFieldUseDenialsHaveNoEffectsAndHealthyCallersStillRead()
    {
        using var deadline = McpCallerDeadline.Create();
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(admin, deadline.Token);
        var right = new ResourceDefinition(RightCollection, ResourceKind.Collection, scenario.Partition.TransactionDomainId)
        {
            RelationalSchema = new(RightId, [new(RightId, RelationalColumnType.Text), new(RightName, RelationalColumnType.Text)]),
            FieldPolicies = [new(RightFieldPath, "join-private", RawUseGrant: RightUseGrant)]
        };
        await ConfigureRightAsync(admin, scenario, right, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(scenario.Command(
            new PutDocument(RelationalSqlClusterTokens.Table, "join-order", "{\"key\":\"join-order\",\"title\":\"join-customer\",\"count\":1}"),
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
        await RelationalSqlClusterJoinPageAssertions.RevisionsAsync(before.Direct, 1, 1);
        using (var healthyJson = System.Text.Json.JsonDocument.Parse(before.Direct.Rows[0].Json))
        {
            await Assert.That(healthyJson.RootElement.GetProperty(CustomerNameField).GetString()).IsEqualTo(PrivateCustomer);
            await Assert.That(healthyJson.RootElement.GetProperty(CustomerIdField).GetString()).IsEqualTo("join-customer");
        }
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(before.Direct, before.Unified);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(before.Direct, before.McpDirect);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(before.Direct, before.McpUnified);
        await AssertDeniedAsync(limitedSdk, limitedMcp, query, sql, limited.Secret, deadline.Token);
        await AssertDeniedAsync(missingUseSdk, missingUseMcp, query, sql, missingUse.Secret, deadline.Token);
        var after = await ReadAdministratorPagesAsync(fixture, admin, query, sql, deadline.Token);
        var afterRows = await ReadRowsAsync(admin, scenario, deadline.Token);
        await SqlClusterProtocol.EqualAsync(beforeRows.Left, afterRows.Left);
        await SqlClusterProtocol.EqualAsync(beforeRows.Right, afterRows.Right);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(before.Direct, after.Direct);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(before.Unified, after.Unified);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(before.McpDirect, after.McpDirect);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(before.McpUnified, after.McpUnified);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(after.Direct, after.Unified);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(after.Direct, after.McpDirect);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(after.Direct, after.McpUnified);
    }

    private static async Task<(DocumentResult? Left, DocumentResult? Right)> ReadRowsAsync(KeyLoadClient admin,
        RelationalSqlClusterScenario scenario, CancellationToken cancellationToken)
    {
        var left = await PublicReadAssertions.DocumentAsync(await admin.GetAsync(
            new(scenario.Partition, RelationalSqlClusterTokens.Table, "join-order"), cancellationToken));
        var right = await PublicReadAssertions.DocumentAsync(await admin.GetAsync(
            new(scenario.Partition, RightCollection, "join-customer"), cancellationToken));
        return (left, right);
    }

    private static async Task ConfigureRightAsync(KeyLoadClient admin, RelationalSqlClusterScenario scenario,
        ResourceDefinition right, CancellationToken cancellationToken)
    {
        var configured = await SqlClusterProtocol.SdkAsync<ResourceDefinition>(admin,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.ResourcesConfigure,
                new ConfigureResourceRequest(scenario.Partition.TenantId, scenario.Partition.DatabaseId, right),
                Guid.NewGuid()), cancellationToken);
        await Assert.That(configured.RelationalSchema!.PrimaryKey).IsEqualTo(RightId);
    }

    private static async Task<McpPersistedIdentity> CreateIdentityAsync(ClusterFixture fixture, KeyLoadClient admin,
        RelationalSqlClusterScenario scenario, Capability rightCapabilities, CancellationToken cancellationToken)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            RelationalSqlClusterTokens.Table, Capability.Query | Capability.DocumentsRead, cancellationToken);
        await Assert.That(identity.Principal.PolicyEpoch).IsEqualTo(InitialPolicyEpoch);
        var principal = identity.Principal with
        {
            Grants = [new(scenario.Partition.DatabaseId, RelationalSqlClusterTokens.Table, Capability.Query | Capability.DocumentsRead),
                new(scenario.Partition.DatabaseId, RightCollection, rightCapabilities)],
            PolicyEpoch = UpdatedPolicyEpoch
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigurePrincipalAsync(Guid.NewGuid(), principal, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(UpdatedPolicyEpoch);
        await SqlClusterProtocol.EqualAsync(principal, persisted);
        return identity with
        {
            Principal = persisted
        };
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
        var directResult = await admin.QueryAsync(query, cancellationToken);
        RelationalSqlClusterOriginalQueryObservation.Failure(directResult);
        var direct = await McpCallerAssertions.SdkSuccessAsync(directResult);
        var unified = await SqlClusterProtocol.SdkAsync<QueryPage>(admin, sql, cancellationToken);
        var mcpDirect = await ReadMcpAsync(mcp, query, cancellationToken);
        var mcpUnified = await SqlClusterProtocol.McpAsync<QueryPage>(mcp, sql, cancellationToken);
        await RelationalSqlClusterJoinPageAssertions.VerifySourcesAsync(direct);
        await RelationalSqlClusterJoinPageAssertions.VerifySourcesAsync(unified);
        await RelationalSqlClusterJoinPageAssertions.VerifySourcesAsync(mcpDirect);
        await RelationalSqlClusterJoinPageAssertions.VerifySourcesAsync(mcpUnified);
        return (direct, unified, mcpDirect, mcpUnified);
    }
}
