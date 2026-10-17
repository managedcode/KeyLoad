using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-AISQL-007: cancelling a SQL read before dispatch preserves healthy real RF3 read progress.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlRf3CancellationTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcAisql007CancelledSdkSelectReturnsCancelledAndTheNextSqlReadReturnsThePersistedRow()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await SqlRf3DeliveryScenario.CreateAsync(sdk, deadline.Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await cancellation.CancelAsync();
        var rejected = await sdk.ExecuteSqlAsync(scenario.Select, cancellation.Token);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.Problem!.ErrorCode).IsEqualTo(nameof(ErrorCode.Cancelled));
        await scenario.VerifyReadAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(sdk, scenario.Select, deadline.Token));
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        await scenario.VerifyReadAsync(await SqlRf3Protocol.McpAsync<QueryPage>(mcp, scenario.Select, deadline.Token));
    }

    [Test]
    public async Task AcAisql007CancelledOfficialMcpSelectThrowsAndTheNextSqlReadReturnsThePersistedRow()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await SqlRf3DeliveryScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await cancellation.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => mcp.CallAsync(SqlOperationProtocol.ToolName,
            scenario.Select, cancellation.Token));
        await scenario.VerifyReadAsync(await SqlRf3Protocol.McpAsync<QueryPage>(mcp, scenario.Select, deadline.Token));
        await scenario.VerifyReadAsync(await SqlRf3Protocol.SdkAsync<QueryPage>(sdk, scenario.Select, deadline.Token));
    }
}
