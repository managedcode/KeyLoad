using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

/// <summary>AC-REL-004-JOIN-001/005/006: public rejection leaves committed rows and healthy Q2 callers intact.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlRf3JoinRejectionTests(ClusterFixture fixture)
{
    private const string Prefix = RelationalSqlRf3JoinRejectionFlow.Prefix;
    private const string Join = RelationalSqlRf3JoinRejectionFlow.Join;
    private const string Order = RelationalSqlRf3JoinRejectionFlow.Order;
    private const int Q2 = RelationalSqlRf3JoinRejectionFlow.Q2;

    [Test]
    [Arguments(Prefix + Join + Order, 1, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + Join + Order, 3, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "LEFT JOIN joincustomers AS r ON l.title = r.id " + Order, Q2, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "RIGHT JOIN joincustomers AS r ON l.title = r.id " + Order, Q2, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "FULL JOIN joincustomers AS r ON l.title = r.id " + Order, Q2, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "CROSS JOIN joincustomers AS r " + Order, Q2, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + "NATURAL JOIN joincustomers AS r " + Order, Q2, ErrorCode.UnsupportedCapability)]
    [Arguments(Prefix + Join + "WHERE l.count = 1 " + Order, Q2, ErrorCode.UnsupportedCapability)]
    [Arguments("SELECT * FROM agentrows AS l " + Join + Order, Q2, ErrorCode.Validation)]
    [Arguments("SELECT l.key AS repeated, r.name AS repeated FROM agentrows AS l " + Join + Order, Q2, ErrorCode.Validation)]
    [Arguments(Prefix + Join + "ORDER BY l.key DESC LIMIT 10", Q2, ErrorCode.Validation)]
    [Arguments(Prefix + Join + "ORDER BY l.count ASC LIMIT 10", Q2, ErrorCode.Validation)]
    public async Task RejectedJoinOnFourPublicPathsPreservesRowsAndCompleteHealthyPage(string text, int dialect, ErrorCode code)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var seed = await RelationalSqlRf3JoinRejectionFlow.SeedAsync(sdk, deadline.Token);
        var scenario = seed.Scenario;
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var rows = await RelationalSqlRf3JoinRejectionFlow.ReadRowsAsync(sdk, scenario, deadline.Token);
        var healthy = await RelationalSqlRf3JoinRejectionFlow.ReadHealthyAsync(sdk, mcp, scenario.Partition, deadline.Token);
        await Assert.That(healthy.CutPosition).IsGreaterThanOrEqualTo(seed.Position);
        var query = new QueryRequest(scenario.Partition, text, AllowFullScan: true, QueryDialectVersion: dialect);
        var sql = new SqlOperationRequest(scenario.Partition, text, AllowFullScan: true, QueryDialectVersion: dialect);
        var direct = await sdk.QueryAsync(query, deadline.Token);
        var unified = await sdk.ExecuteSqlAsync(sql, deadline.Token);
        await Assert.That(direct.IsSuccess).IsFalse();
        await Assert.That(unified.IsSuccess).IsFalse();
        await Assert.That(direct.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await Assert.That(unified.Problem?.ErrorCode).IsEqualTo(code.ToString());
        await AssertSafeProblemAsync(direct.Problem, fixture.AdminKey);
        await AssertSafeProblemAsync(unified.Problem, fixture.AdminKey);
        var officialQuery = await mcp.CallAsync(McpCallerTools.QueryExecute, query, deadline.Token);
        var officialSql = await mcp.CallAsync(SqlOperationProtocol.ToolName, sql, deadline.Token);
        await McpCallerAssertions.ErrorAsync(officialQuery, code, dispatched: true);
        await McpCallerAssertions.ErrorAsync(officialSql, code, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(officialQuery, fixture.AdminKey, RelationalSqlRf3JoinRejectionFlow.PrivateName);
        await McpCallerAssertions.DoesNotDiscloseAsync(officialSql, fixture.AdminKey, RelationalSqlRf3JoinRejectionFlow.PrivateName);
        await RelationalSqlRf3JoinRejectionFlow.VerifyRowsAsync(sdk, scenario, rows, deadline.Token);
        var after = await RelationalSqlRf3JoinRejectionFlow.ReadHealthyAsync(sdk, mcp, scenario.Partition, deadline.Token);
        await RelationalSqlRf3JoinPageAssertions.SameRowsAsync(healthy, after);
    }

    private static async Task AssertSafeProblemAsync<T>(T problem, string secret)
    {
        var json = JsonSerializer.Serialize(problem, JsonDefaults.Options);
        await Assert.That(json.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains(RelationalSqlRf3JoinRejectionFlow.PrivateName, StringComparison.Ordinal)).IsFalse();
    }
}
