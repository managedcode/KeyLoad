using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.RelationalStorage;

/// <summary>AC-REL-004-JOIN-001/005/006: public rejection leaves committed rows and healthy Q2 callers intact.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class RelationalSqlClusterJoinRejectionTests(ClusterFixture fixture)
{
    private const string Prefix = RelationalSqlClusterJoinRejectionFlow.Prefix;
    private const string Join = RelationalSqlClusterJoinRejectionFlow.Join;
    private const string Order = RelationalSqlClusterJoinRejectionFlow.Order;
    private const int Dialect = RelationalSqlClusterJoinRejectionFlow.Dialect;
    private const int NextDialect = Dialect + 1;
    private const int LaterDialect = Dialect + 2;

    [Test]
    [Arguments(Prefix + Join + Order, NextDialect, ErrorCode.UnsupportedCapability, true)]
    [Arguments(Prefix + Join + Order, LaterDialect, ErrorCode.UnsupportedCapability, true)]
    [Arguments(Prefix + "LEFT JOIN joincustomers AS r ON l.title = r.id " + Order, Dialect, ErrorCode.UnsupportedCapability, true)]
    [Arguments(Prefix + "RIGHT JOIN joincustomers AS r ON l.title = r.id " + Order, Dialect, ErrorCode.UnsupportedCapability, true)]
    [Arguments(Prefix + "FULL JOIN joincustomers AS r ON l.title = r.id " + Order, Dialect, ErrorCode.UnsupportedCapability, true)]
    [Arguments(Prefix + "CROSS JOIN joincustomers AS r " + Order, Dialect, ErrorCode.UnsupportedCapability, true)]
    [Arguments(Prefix + "NATURAL JOIN joincustomers AS r " + Order, Dialect, ErrorCode.Validation, true)]
    [Arguments(Prefix + Join + "WHERE r.name = 'Ada' " + Order, Dialect, ErrorCode.UnsupportedCapability, true)]
    [Arguments("SELECT TOP (10) * FROM agentrows AS l " + Join + Order, Dialect, ErrorCode.UnsupportedCapability, true)]
    [Arguments("SELECT TOP (10) l.[key] AS repeated, r.name AS repeated FROM agentrows AS l " + Join + Order, Dialect, ErrorCode.Validation, true)]
    [Arguments(Prefix + Join + "ORDER BY l.[key] DESC", Dialect, ErrorCode.Validation, false)]
    [Arguments(Prefix + Join + "ORDER BY l.count ASC", Dialect, ErrorCode.Validation, false)]
    public async Task RejectedJoinOnFourPublicPathsPreservesRowsAndCompleteHealthyPage(string text, int dialect, ErrorCode code,
        bool refusedBeforeDispatch)
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var seed = await RelationalSqlClusterJoinRejectionFlow.SeedAsync(sdk, deadline.Token);
        var scenario = seed.Scenario;
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var rows = await RelationalSqlClusterJoinRejectionFlow.ReadRowsAsync(sdk, scenario, seed.RightLifetime, deadline.Token);
        var healthy = await RelationalSqlClusterJoinRejectionFlow.ReadHealthyAsync(sdk, mcp, scenario.Partition, deadline.Token);
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
        await McpCallerAssertions.ErrorAsync(officialSql, code, dispatched: !refusedBeforeDispatch);
        await McpCallerAssertions.DoesNotDiscloseAsync(officialQuery, fixture.AdminKey, RelationalSqlClusterJoinRejectionFlow.PrivateName);
        await McpCallerAssertions.DoesNotDiscloseAsync(officialSql, fixture.AdminKey, RelationalSqlClusterJoinRejectionFlow.PrivateName);
        await RelationalSqlClusterJoinRejectionFlow.VerifyRowsAsync(sdk, scenario, seed.RightLifetime, rows, deadline.Token);
        var after = await RelationalSqlClusterJoinRejectionFlow.ReadHealthyAsync(sdk, mcp, scenario.Partition, deadline.Token);
        await RelationalSqlClusterJoinPageAssertions.SameRowsAsync(healthy, after);
    }

    private static async Task AssertSafeProblemAsync<T>(T problem, string secret)
    {
        var json = JsonSerializer.Serialize(problem, JsonDefaults.Options);
        await Assert.That(json.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(json.Contains(RelationalSqlClusterJoinRejectionFlow.PrivateName, StringComparison.Ordinal)).IsFalse();
    }
}
