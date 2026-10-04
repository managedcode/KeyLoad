using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>AC-AISQL-002–006: typed rows retain canonical RF3 schema, retry, patch and SELECT behavior.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlRf3RowsTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcAisql004SqlTypedPutRetriesAndPatchShareNativeReceiptsAndIndexedSelect()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        var command = scenario.Command(new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId,
            RelationalSqlRf3Tokens.FirstRow));
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var first = await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, deadline.Token);
        var nativeRetry = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token));
        var sqlRetry = await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, deadline.Token);
        await SqlRf3Protocol.EqualAsync(first, nativeRetry);
        await SqlRf3Protocol.EqualAsync(first, sqlRetry);
        await Assert.That(first.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(first.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var patch = scenario.Command(new PatchDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId,
            [new(RelationalSqlRf3Tokens.CountPath, PatchKind.Set, RelationalSqlRf3Tokens.PatchValue)], RelationalSqlRf3Tokens.FirstRevision));
        var patched = await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp,
            SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, patch), deadline.Token);
        await Assert.That(patched.Token.Position).IsGreaterThan(first.Token.Position);
        var row = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        await Assert.That(row!.Revision).IsEqualTo(RelationalSqlRf3Tokens.PatchedRevision);
        using var json = JsonDocument.Parse(row.Json);
        await Assert.That(json.RootElement.GetProperty(RelationalSqlRf3Tokens.Count).GetInt32()).IsEqualTo(RelationalSqlRf3Tokens.PatchedCount);
        await VerifySelectAsync(sdk, mcp, scenario, patched.Token.Position, deadline.Token);
    }

    [Test]
    public async Task AcAisql003SqlInvalidTypedPatchLeavesTheFinalRowAndRevisionUnchanged()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(scenario.Command(
            new PutDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId, RelationalSqlRf3Tokens.FirstRow)), deadline.Token));
        var before = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        var command = scenario.Command(new PatchDocument(RelationalSqlRf3Tokens.Table, RelationalSqlRf3Tokens.FirstId,
            [new(RelationalSqlRf3Tokens.CountPath, PatchKind.Set, RelationalSqlRf3Tokens.InvalidPatchValue)], RelationalSqlRf3Tokens.FirstRevision));
        var sql = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var failed = await sdk.ExecuteSqlAsync(sql, deadline.Token);
        await Assert.That(failed.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, deadline.Token), ErrorCode.Validation, dispatched: true);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        await SqlRf3Protocol.EqualAsync(before, after);
    }

    private static async Task VerifySelectAsync(KeyLoadClient sdk, McpOfficialClient mcp, RelationalSqlRf3Scenario scenario,
        long committedPosition, CancellationToken cancellationToken)
    {
        var request = new SqlOperationRequest(scenario.Partition, SqlRf3Protocol.TableSelect);
        var sql = await SqlRf3Protocol.SdkAsync<QueryPage>(sdk, request, cancellationToken);
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(
            new(scenario.Partition, SqlRf3Protocol.TableSelect), cancellationToken));
        var official = await SqlRf3Protocol.McpAsync<QueryPage>(mcp, request, cancellationToken);
        await Assert.That(sql.AccessPath).IsEqualTo(SqlRf3Protocol.IndexPath);
        await Assert.That(sql.Rows).HasSingleItem();
        await Assert.That(sql.Rows[0].EntityId).IsEqualTo(RelationalSqlRf3Tokens.FirstId);
        await Assert.That(sql.Rows[0].Revision).IsEqualTo(RelationalSqlRf3Tokens.PatchedRevision);
        await SqlRf3Protocol.EqualAsync(sql.Rows, native.Rows);
        await SqlRf3Protocol.EqualAsync(sql.Rows, official.Rows);
        await Assert.That(native.AccessPath).IsEqualTo(sql.AccessPath);
        await Assert.That(official.AccessPath).IsEqualTo(sql.AccessPath);
        await Assert.That(sql.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
        await Assert.That(native.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
        await Assert.That(official.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
    }
}
