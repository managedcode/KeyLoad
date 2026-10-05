using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-SQLC-003: genuine RF3 SQL comments preserve canonical execution and replay.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlRf3CommentTests(ClusterFixture fixture)
{
    private const string Prefix = "-- client\r\n/* outer /* nested */ ; CALL ignored */";
    private const string Suffix = ";/* complete */-- EOF";
    private const string Call = "CALL/* dispatch */ ";
    private const string Arguments = "/* name */(/* bind */@args/* value */)";
    private const string CommentedSelect = "/* SELECT */SELECT/* fields */ * FROM agentrows-- source\r\n"
        + "WHERE 'alpha'/* value */ = title ORDER/* order */ BY id;-- EOF";
    private const string Unclosed = "/* missing close";
    private const string SecondStatement = "; CALL keyload_query_capabilities(@args)";

    [Test]
    public async Task AC_SQLC_003_CommentedWriteReplaysAndReadsThroughRealSdkAndOfficialMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        var command = scenario.LinkedModelsCommand();
        var plain = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        var sql = plain with { Sql = Prefix + Call + McpCallerTools.DocumentsCommit + Arguments + Suffix };
        var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, sql, deadline.Token);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        await SqlRf3Protocol.EqualAsync(receipt, await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, deadline.Token));
        await SqlRf3Protocol.EqualAsync(receipt, await McpCallerAssertions.SdkSuccessAsync(
            await sdk.CommitAsync(command, deadline.Token)));
        await VerifyRowsAsync(sdk, mcp, scenario, receipt.Token.Position, deadline.Token);
    }

    [Test]
    public async Task AC_SQLC_003_MalformedCommentsAndSecondStatementCannotConsumeTheStableWrite()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        var command = scenario.LinkedModelsCommand();
        var request = SqlRf3Protocol.Call(scenario.Partition, McpCallerTools.DocumentsCommit, command);
        foreach (var sql in new[] { Unclosed + request.Sql, request.Sql + SecondStatement })
        {
            var rejected = await sdk.ExecuteSqlAsync(request with { Sql = sql }, deadline.Token);
            await Assert.That(rejected.IsFailed).IsTrue();
            await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
        }
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(
            await sdk.GetAsync(scenario.First, deadline.Token))).IsNull();
        var receipt = await SqlRf3Protocol.SdkAsync<CommitReceipt>(sdk, request, deadline.Token);
        await Assert.That(receipt.CommandId).IsEqualTo(command.CommandId);
        var after = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.First, deadline.Token));
        await Assert.That(after).IsNotNull();
        await Assert.That(after!.Revision).IsEqualTo(RelationalSqlRf3Tokens.FirstRevision);
    }

    private static async Task VerifyRowsAsync(KeyLoadClient sdk, McpOfficialClient mcp, RelationalSqlRf3Scenario scenario,
        long committedPosition, CancellationToken cancellationToken)
    {
        var plain = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAsync(
            new(scenario.Partition, SqlRf3Protocol.TableSelect), cancellationToken));
        var request = new SqlOperationRequest(scenario.Partition, Prefix + CommentedSelect);
        var sdkPage = await SqlRf3Protocol.SdkAsync<QueryPage>(sdk, request, cancellationToken);
        var mcpPage = await SqlRf3Protocol.McpAsync<QueryPage>(mcp, request, cancellationToken);
        await Assert.That(plain.Rows).HasSingleItem();
        await Assert.That(plain.Rows[0].EntityId).IsEqualTo(RelationalSqlRf3Tokens.FirstId);
        foreach (var page in new[] { plain, sdkPage, mcpPage })
        {
            await Assert.That(page.AccessPath).IsEqualTo(SqlRf3Protocol.IndexPath);
            await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(committedPosition);
            await SqlRf3Protocol.EqualAsync(plain.Rows, page.Rows);
        }
    }
}
