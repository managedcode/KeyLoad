using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-SQLVIEW-003: event model rows honor persisted payload/header policy.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlModelViewRf3EventAuthorityTests(ClusterFixture fixture)
{
    [Test]
    public async Task PersistedEventGrantsRedactCanariesDenyFieldUseAndHonorRevocation()
    {
        using var deadline = McpCallerDeadline.Create();
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await SqlModelViewRf3Scenario.CreateAsync(administrator, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            SqlModelViewRf3Scenario.StreamSet, Capability.Query | Capability.EventsRead, deadline.Token);
        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var reader = new KeyLoadClient(readerHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);

        await SqlModelViewRf3ForeignTenantFlow.RunAsync(administrator, reader, mcp, identity.Secret, deadline.Token);

        var request = scenario.EventSql();
        var redacted = await McpCallerAssertions.SdkSuccessAsync(await reader.QueryAsync(request, deadline.Token));
        var redactedMcp = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, request, deadline.Token));
        await SqlModelViewRf3Assertions.AssertRedactedRowsAsync(redacted,
            SqlModelViewRf3Scenario.EventCanary, SqlModelViewRf3Scenario.EventHeaderCanary);
        await SqlRf3Protocol.EqualAsync(redacted.Rows, redactedMcp.Value.Rows);

        var deniedUse = request with
        {
            Sql = $"SELECT * FROM EVENTS('{SqlModelViewRf3Scenario.StreamSet}','{SqlModelViewRf3Scenario.StreamId}') "
                + $"WHERE payload.secret = '{SqlModelViewRf3Scenario.EventCanary}' LIMIT {SqlModelViewRf3Scenario.RowLimit}"
        };
        var denied = await reader.QueryAsync(deniedUse, deadline.Token);
        await SqlModelViewRf3DenialAssertions.SdkAsync(denied, identity.Secret);
        var deniedMcp = await mcp.CallAsync(McpCallerTools.QueryExecute, deniedUse, deadline.Token);
        await SqlModelViewRf3DenialAssertions.McpAsync(deniedMcp, identity.Secret);

        identity = await SqlModelViewRf3AuthorizationAssertions.GrantSensitiveReadAsync(
            administrator, identity, deadline.Token);
        var visible = await McpCallerAssertions.SdkSuccessAsync(await reader.QueryAsync(
            SqlModelViewRf3AuthorizationAssertions.EventPrivateProjection(scenario), deadline.Token));
        var visibleMcp = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, SqlModelViewRf3AuthorizationAssertions.EventPrivateProjection(scenario), deadline.Token));
        await SqlRf3Protocol.EqualAsync(visible.Rows, visibleMcp.Value.Rows);
        await Assert.That(visible.Rows.Length).IsEqualTo(1);
        using var visibleJson = JsonDocument.Parse(visible.Rows[0].Json);
        await Assert.That(visibleJson.RootElement.GetProperty(SqlModelViewRf3JsonKeys.Secret).GetString())
            .IsEqualTo(SqlModelViewRf3Scenario.EventCanary);

        await SqlModelViewRf3AuthorizationAssertions.RevokeAsync(administrator, identity, deadline.Token);
        await SqlModelViewRf3AuthorizationAssertions.AssertDeniedAsync(fixture, reader, mcp, request,
            identity.Secret, deadline.Token);
    }
}
