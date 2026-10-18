using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Executes the caller-visible cross-partition identity flow on the Aspire-owned Orleans cluster.</summary>
internal static class ScopedCommandIdentityClusterWorkflow
{
    private const string FirstEntity = "partition-a-document";
    private const string SecondEntity = "partition-b-document";
    private const string FirstJson = "{\"value\":\"partition-a-original\"}";
    private const string SecondJson = "{\"value\":\"partition-b-original\"}";
    private const string ChangedFirstJson = "{\"value\":\"partition-a-changed\"}";
    private const string ChangedSecondJson = "{\"value\":\"partition-b-changed\"}";
    private const string RestartScenario = "dstore-outcome-v2-reopen";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, ScopedCommandIdentityClusterScenario scenario,
        CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid();
        var first = scenario.CreateCommand(commandId, scenario.FirstPartition, FirstEntity, FirstJson);
        var second = scenario.CreateCommand(commandId, scenario.SecondPartition, SecondEntity, SecondJson);
        CommitReceipt firstReceipt;
        CommitReceipt secondReceipt;
        using (var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1))
        {
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
                fixture.AdminKey, cancellationToken);
            var firstBefore = await ScopedCommandIdentityClusterOracle.BeforeCommandAsync(sdk, scenario,
                commandId, first, true, cancellationToken);
            firstReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(first, cancellationToken));
            await ScopedCommandIdentityClusterOracle.ReceiptAsync(sdk, firstReceipt, scenario, commandId, true,
                firstBefore, cancellationToken);
            var secondBefore = await ScopedCommandIdentityClusterOracle.BeforeCommandAsync(sdk, scenario,
                commandId, second, false, cancellationToken);
            secondReceipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.DocumentsCommit, second, cancellationToken))).Value;
            await ScopedCommandIdentityClusterOracle.ReceiptAsync(sdk, secondReceipt, scenario, commandId, false,
                secondBefore, cancellationToken);
            await ScopedCommandIdentityClusterAssertions.AssertRetryAsync(mcp, first, firstReceipt, cancellationToken);
            await ScopedCommandIdentityClusterAssertions.AssertRetryAsync(sdk, second, secondReceipt, cancellationToken);
            await Assert.That(JsonDefaults.Serialize(firstReceipt).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(secondReceipt))).IsFalse();
            await ScopedCommandIdentityClusterAssertions.AssertConflictAsync(mcp, sdk, scenario, first,
                ChangedFirstJson, cancellationToken);
            await ScopedCommandIdentityClusterAssertions.AssertConflictAsync(mcp, sdk, scenario, second,
                ChangedSecondJson, cancellationToken);
            await ScopedCommandIdentityClusterAssertions.AssertStateAsync(sdk, mcp, scenario, firstReceipt, secondReceipt, cancellationToken);
        }

        await RestartAllSilosAsync(fixture, cancellationToken);
        using var reopenedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var reopenedSdk = new KeyLoadClient(reopenedHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
        await using var reopenedMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, cancellationToken);
        await ScopedCommandIdentityClusterAssertions.AssertRetryAsync(reopenedMcp, first, firstReceipt, cancellationToken);
        await ScopedCommandIdentityClusterAssertions.AssertRetryAsync(reopenedSdk, second, secondReceipt, cancellationToken);
        await ScopedCommandIdentityClusterAssertions.AssertStateAsync(reopenedSdk, reopenedMcp, scenario, firstReceipt, secondReceipt, cancellationToken);
    }

    private static async Task RestartAllSilosAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        foreach (var node in Nodes)
        {
            await fixture.KillContainerAsync(node, RestartScenario, cancellationToken);
            await fixture.RestartContainerAsync(node, cancellationToken);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken);
        }
    }
}
