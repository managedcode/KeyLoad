using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>AC-AISQL-002–006: typed rows retain canonical CLUSTER schema, retry, patch and SELECT behavior.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlClusterRowsTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcAisql004SqlTypedPutRetriesAndPatchShareNativeReceiptsAndIndexedSelect()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        var command = scenario.Command(new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId,
            RelationalSqlClusterTokens.FirstRow));
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var first = await SqlClusterProtocol.SdkAsync<CommitReceipt>(sdk, sql, deadline.Token);
        var nativeRetry = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token));
        var sqlRetry = await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, sql, deadline.Token);
        await SqlClusterProtocol.EqualAsync(first, nativeRetry);
        await SqlClusterProtocol.EqualAsync(first, sqlRetry);
        await Assert.That(first.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(first.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var patch = scenario.Command(new PatchDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId,
            [new(RelationalSqlClusterTokens.CountPath, PatchKind.Set, RelationalSqlClusterTokens.PatchValue)], RelationalSqlClusterTokens.FirstRevision));
        var patched = await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp,
            SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, patch), deadline.Token);
        await Assert.That(patched.Token.Position).IsGreaterThan(first.Token.Position);
        var row = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        await Assert.That(row!.Revision).IsEqualTo(RelationalSqlClusterTokens.PatchedRevision);
        using var json = JsonDocument.Parse(row.Json);
        await Assert.That(json.RootElement.GetProperty(RelationalSqlClusterTokens.Count).GetInt32()).IsEqualTo(RelationalSqlClusterTokens.PatchedCount);
        await VerifySelectAsync(sdk, mcp, scenario, patched.Token.Position, deadline.Token);
    }

    [Test]
    public async Task AcAisql003SqlInvalidTypedPatchLeavesTheFinalRowAndRevisionUnchanged()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Command(
            new PutDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId, RelationalSqlClusterTokens.FirstRow)), deadline.Token));
        var before = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        var command = scenario.Command(new PatchDocument(RelationalSqlClusterTokens.Table, RelationalSqlClusterTokens.FirstId,
            [new(RelationalSqlClusterTokens.CountPath, PatchKind.Set, RelationalSqlClusterTokens.InvalidPatchValue)], RelationalSqlClusterTokens.FirstRevision));
        var sql = SqlClusterProtocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var failed = await sdk.ExecuteSqlAsync(sql, deadline.Token);
        await Assert.That(failed.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, deadline.Token), ErrorCode.Validation, dispatched: true);
        var after = await PublicReadAssertions.DocumentAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        await SqlClusterProtocol.EqualAsync(before, after);
    }

    private static async Task VerifySelectAsync(KeyLoadClient sdk, McpOfficialClient mcp, RelationalSqlClusterScenario scenario,
        long committedPosition, CancellationToken cancellationToken)
    {
        var request = new SqlOperationRequest(scenario.Partition, SqlClusterProtocol.TableSelect);
        var sql = await SqlClusterProtocol.SdkAsync<QueryPage>(sdk, request, cancellationToken);
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(
            new(scenario.Partition, SqlClusterProtocol.TableSelect), cancellationToken));
        var official = await SqlClusterProtocol.McpAsync<QueryPage>(mcp, request, cancellationToken);
        await Assert.That(sql.AccessPath).IsEqualTo(SqlClusterProtocol.IndexPath);
        await Assert.That(sql.Rows).HasSingleItem();
        await Assert.That(sql.Rows[0].EntityId).IsEqualTo(RelationalSqlClusterTokens.FirstId);
        await Assert.That(sql.Rows[0].Revision).IsEqualTo(RelationalSqlClusterTokens.PatchedRevision);
        await SqlClusterProtocol.EqualAsync(sql.Rows, native.Rows);
        await SqlClusterProtocol.EqualAsync(sql.Rows, official.Rows);
        await Assert.That(native.AccessPath).IsEqualTo(sql.AccessPath);
        await Assert.That(official.AccessPath).IsEqualTo(sql.AccessPath);
        await Assert.That(sql.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
        await Assert.That(native.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
        await Assert.That(official.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
    }
}
