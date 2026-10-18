using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>AC-AISQL-006: SQL preserves the same persisted read/write grants as native callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlClusterAuthorizationTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcAisql006ReadOnlyPersistedIdentityCannotWriteThroughSqlOrOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(administrator, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(scenario.Command(new PutDocument(
            RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.FirstRow)), deadline.Token));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, RelationalSqlClusterTokens.Table,
            Capability.DocumentsRead | Capability.Query, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, identity.Secret, deadline.Token);
        var select = new SqlOperationRequest(scenario.Partition, SqlClusterProtocol.TableSelect);
        var read = await SqlClusterProtocol.SdkAsync<QueryPage>(sdk, select, deadline.Token);
        var officialRead = await SqlClusterProtocol.McpAsync<QueryPage>(mcp, select, deadline.Token);
        await Assert.That(read.Rows).HasSingleItem();
        await SqlClusterProtocol.EqualAsync(read.Rows, officialRead.Rows);
        var command = scenario.Command(new PatchDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId,
            [new(RelationalSqlClusterTokens.CountPath, PatchKind.Set, RelationalSqlClusterTokens.PatchValue)], RelationalSqlClusterTokens.FirstRevision));
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        await Assert.That((await sdk.ExecuteSqlAsync(sql, deadline.Token)).Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await Assert.That((await sdk.CommitAsync(command, deadline.Token)).Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var denied = await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, deadline.Token);
        await McpCallerAssertions.ErrorAsync(denied, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, identity.Secret, RelationalSqlClusterTokens.FirstRow);
        var after = await SqlClusterProtocol.McpAsync<QueryPage>(mcp, select, deadline.Token);
        await SqlClusterProtocol.EqualAsync(read.Rows, after.Rows);
        await Assert.That(after.Rows[0].Revision).IsEqualTo(RelationalSqlClusterTokens.FirstRevision);
    }
}
