using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Executes the caller-visible cross-partition identity flow on the Aspire-owned RF3 cluster.</summary>
internal static class ScopedCommandIdentityRf3Workflow
{
    private const string FirstEntity = "partition-a-document";
    private const string SecondEntity = "partition-b-document";
    private const string FirstJson = "{\"value\":\"partition-a-original\"}";
    private const string SecondJson = "{\"value\":\"partition-b-original\"}";
    private const string ChangedFirstJson = "{\"value\":\"partition-a-changed\"}";
    private const string ChangedSecondJson = "{\"value\":\"partition-b-changed\"}";
    private const string RestartScenario = "dstore-outcome-v2-reopen";
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];

    internal static async Task RunAsync(ClusterFixture fixture, ScopedCommandIdentityRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var commandId = Guid.NewGuid();
        var first = scenario.CreateCommand(commandId, scenario.FirstPartition, FirstEntity, FirstJson);
        var second = scenario.CreateCommand(commandId, scenario.SecondPartition, SecondEntity, SecondJson);
        CommitReceipt firstReceipt;
        CommitReceipt secondReceipt;
        using (var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1))
        {
            var sdk = new KeyLoadClient(http, fixture.AdminKey);
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
                fixture.AdminKey, cancellationToken);
            firstReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(first, cancellationToken));
            secondReceipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.DocumentsCommit, second, cancellationToken))).Value;
            await ScopedCommandIdentityRf3Assertions.AssertReceiptAsync(firstReceipt, scenario, FirstEntity);
            await ScopedCommandIdentityRf3Assertions.AssertReceiptAsync(secondReceipt, scenario, SecondEntity);
            await ScopedCommandIdentityRf3Assertions.AssertRetryAsync(mcp, first, firstReceipt, cancellationToken);
            await ScopedCommandIdentityRf3Assertions.AssertRetryAsync(sdk, second, secondReceipt, cancellationToken);
            await Assert.That(JsonDefaults.Serialize(firstReceipt).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(secondReceipt))).IsFalse();
            await ScopedCommandIdentityRf3Assertions.AssertConflictAsync(mcp, sdk, scenario, first,
                ChangedFirstJson, cancellationToken);
            await ScopedCommandIdentityRf3Assertions.AssertConflictAsync(mcp, sdk, scenario, second,
                ChangedSecondJson, cancellationToken);
            await ScopedCommandIdentityRf3Assertions.AssertStateAsync(sdk, mcp, scenario, FirstEntity, FirstJson,
                SecondEntity, SecondJson, cancellationToken);
        }

        await RestartAllVotersAsync(fixture, cancellationToken);
        using var reopenedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var reopenedSdk = new KeyLoadClient(reopenedHttp, fixture.AdminKey);
        await using var reopenedMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, cancellationToken);
        await ScopedCommandIdentityRf3Assertions.AssertRetryAsync(reopenedMcp, first, firstReceipt, cancellationToken);
        await ScopedCommandIdentityRf3Assertions.AssertRetryAsync(reopenedSdk, second, secondReceipt, cancellationToken);
        await ScopedCommandIdentityRf3Assertions.AssertStateAsync(reopenedSdk, reopenedMcp, scenario, FirstEntity,
            FirstJson, SecondEntity, SecondJson, cancellationToken);
    }

    private static async Task RestartAllVotersAsync(ClusterFixture fixture, CancellationToken cancellationToken)
    {
        foreach (var node in Nodes)
        {
            await fixture.KillContainerAsync(node, RestartScenario, cancellationToken);
            await fixture.RestartContainerAsync(node, cancellationToken);
            await fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(node, cancellationToken);
        }
    }
}
