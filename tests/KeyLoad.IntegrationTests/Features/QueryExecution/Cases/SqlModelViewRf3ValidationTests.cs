using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-SQLVIEW-002: grants and invalid source arguments fail before model scanning.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlModelViewRf3ValidationTests(ClusterFixture fixture)
{
    [Test]
    public async Task EachModelRequiresBothPersistedGrants()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await SqlModelViewRf3Scenario.CreateAsync(administrator, deadline.Token);

        await AssertMissingAsync(scenario, Capability.EventsRead, scenario.EventSql(), deadline.Token);
        await AssertMissingAsync(scenario, Capability.Query, scenario.EventSql(), deadline.Token);
        await AssertMissingAsync(scenario, Capability.QueueInspect, scenario.QueueSql(), deadline.Token);
        await AssertMissingAsync(scenario, Capability.Query, scenario.QueueSql(), deadline.Token);
    }

    [Test]
    public async Task InvalidSourcesAndModelCursorRejectBeforeScanning()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await SqlModelViewRf3Scenario.CreateAsync(administrator, deadline.Token);
        await AssertStaleEventAsync(administrator, scenario, deadline.Token);
        await AssertSecondQueueLaneAsync(administrator, scenario, deadline.Token);
        await AssertUnsupportedCursorAsync(administrator, scenario, deadline.Token);
        var unaffected = await McpCallerAssertions.SdkSuccessAsync(await administrator.QueryAsync(
            scenario.QueueSql(), deadline.Token));
        await Assert.That(unaffected.Rows.Length).IsEqualTo(2);
    }

    private async Task AssertMissingAsync(SqlModelViewRf3Scenario scenario, Capability onlyCapability,
        QueryRequest request, CancellationToken cancellationToken)
        => await SqlModelViewRf3AuthorizationAssertions.AssertMissingGrantDeniedAsync(
            fixture, scenario, onlyCapability, request, cancellationToken);

    private static async Task AssertStaleEventAsync(KeyLoadClient sdk,
        SqlModelViewRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var result = await sdk.QueryAsync(scenario.EventSql(generation: 2), cancellationToken);
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
    }

    private static async Task AssertSecondQueueLaneAsync(KeyLoadClient sdk,
        SqlModelViewRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var sql = scenario.QueueSql() with
        {
            Sql = $"SELECT * FROM QUEUE_MESSAGES('{SqlModelViewRf3Scenario.Queue}','other-lane') "
                + $"LIMIT {SqlModelViewRf3Scenario.RowLimit}"
        };
        var sqlResult = await sdk.QueryAsync(sql, cancellationToken);
        await Assert.That(sqlResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));

        var ast = scenario.QueueAst() with
        {
            Query = scenario.QueueAst().Query with
            { ModelSource = new(ModelQuerySourceKind.QueueMessages, "other-lane") }
        };
        var astResult = await sdk.QueryAstAsync(ast, cancellationToken);
        await Assert.That(astResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Validation));
    }

    private static async Task AssertUnsupportedCursorAsync(KeyLoadClient sdk,
        SqlModelViewRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var request = scenario.EventAst() with { Cursor = "unsupported-model-cursor" };
        var result = await sdk.QueryAstAsync(request, cancellationToken);
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.UnsupportedCapability));
    }
}
