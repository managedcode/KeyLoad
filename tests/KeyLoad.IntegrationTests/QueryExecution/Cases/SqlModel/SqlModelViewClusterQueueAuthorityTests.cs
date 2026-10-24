using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

/// <summary>AC-SQLVIEW-003: queue model rows honor persisted payload/header policy.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlModelViewClusterQueueAuthorityTests(ClusterFixture fixture)
{
    [Test]
    public async Task PersistedQueueGrantsRedactCanariesDenyFieldUseAndHonorRevocation()
    {
        using var deadline = McpCallerDeadline.Create();
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await SqlModelViewClusterScenario.CreateAsync(administrator, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            SqlModelViewClusterScenario.Queue, Capability.Query | Capability.QueueInspect, deadline.Token);
        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var reader = new KeyLoadClient(readerHttp, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);

        await SqlModelViewClusterForeignTenantFlow.RunAsync(administrator, reader, mcp, identity.Secret, deadline.Token);

        var request = scenario.QueueSql();
        var redacted = await McpCallerAssertions.SdkSuccessAsync(await reader.QueryAsync(request, deadline.Token));
        var redactedMcp = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, request, deadline.Token));
        await SqlModelViewClusterAssertions.AssertRedactedRowsAsync(redacted,
            SqlModelViewClusterScenario.QueueCanary, SqlModelViewClusterScenario.QueueHeaderCanary);
        await SqlClusterProtocol.EqualAsync(redacted.Rows, redactedMcp.Value.Rows);

        var deniedUse = request with
        {
            Sql = $"SELECT TOP ({SqlModelViewClusterScenario.RowLimit}) * FROM QUEUE_MESSAGES('{SqlModelViewClusterScenario.Queue}') "
                + $"WHERE payload.secret = '{SqlModelViewClusterScenario.QueueCanary}'"
        };
        var denied = await reader.QueryAsync(deniedUse, deadline.Token);
        await SqlModelViewClusterDenialAssertions.SdkAsync(denied, identity.Secret);
        var deniedMcp = await mcp.CallAsync(McpCallerTools.QueryExecute, deniedUse, deadline.Token);
        await SqlModelViewClusterDenialAssertions.McpAsync(deniedMcp, identity.Secret);

        identity = await SqlModelViewClusterAuthorizationAssertions.VerifyQueueReadWithoutUseAsync(
            administrator, reader, mcp, scenario, identity, deadline.Token);

        identity = await SqlModelViewClusterAuthorizationAssertions.GrantSensitiveReadAsync(
            administrator, identity, deadline.Token);
        var visible = await McpCallerAssertions.SdkSuccessAsync(await reader.QueryAsync(
            SqlModelViewClusterAuthorizationAssertions.QueuePrivateProjection(scenario), deadline.Token));
        var visibleMcp = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(
            McpCallerTools.QueryExecute, SqlModelViewClusterAuthorizationAssertions.QueuePrivateProjection(scenario), deadline.Token));
        await SqlClusterProtocol.EqualAsync(visible.Rows, visibleMcp.Value.Rows);
        await Assert.That(visible.Rows.Length).IsEqualTo(1);
        using var visibleJson = JsonDocument.Parse(visible.Rows[0].Json);
        await Assert.That(visibleJson.RootElement.GetProperty(SqlModelViewClusterJsonKeys.Secret).GetString())
            .IsEqualTo(SqlModelViewClusterScenario.QueueCanary);

        await SqlModelViewClusterAuthorizationAssertions.RevokeAsync(administrator, identity, deadline.Token);
        await SqlModelViewClusterAuthorizationAssertions.AssertDeniedAsync(fixture, reader, mcp, request,
            identity.Secret, deadline.Token);
    }
}
