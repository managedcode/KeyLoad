using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class RuntimeConstructorCycleClusterRefusal
{
    internal static async Task RunAsync(ClusterFixture fixture, KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, Guid id, DateTimeOffset dueAt,
        CommandRequest original, CommitReceipt receipt, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(
                new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution()),
                sdk, mcp, scenario, id, dueAt, original, receipt, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(KeyLoadClient administrator, KeyLoadClient sdk, McpOfficialClient mcp,
        MessagingClusterScenario scenario, Guid id, DateTimeOffset dueAt,
        CommandRequest original, CommitReceipt receipt, CancellationToken token)
    {
        var before = await McpCallerAssertions.SdkSuccessAsync(await administrator.OutboxStatusAsync(scenario.SourcePartition, token));
        await RuntimeConstructorCycleClusterState.TerminalAsync(sdk, mcp, scenario, id, dueAt, token);
        var stale = RecurringSagaClusterSupport.Command(Guid.NewGuid(), scenario.SourcePartition,
            new ExpireSaga(scenario.SourceQueue, id, 1L));
        var sdkFailure = await sdk.CommitAsync(stale, token);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(sdkFailure, ErrorCode.RevisionConflict);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, stale, token),
            ErrorCode.RevisionConflict, dispatched: true);
        await RuntimeConstructorCycleClusterState.TerminalAsync(sdk, mcp, scenario, id, dueAt, token);
        await RuntimeConstructorCycleClusterReceipt.ReplayAsync(sdk, mcp, original, receipt, token);
        await RuntimeConstructorCycleClusterState.TerminalAsync(sdk, mcp, scenario, id, dueAt, token);
        var after = await McpCallerAssertions.SdkSuccessAsync(await administrator.OutboxStatusAsync(scenario.SourcePartition, token));
        await RuntimeConstructorCycleClusterReceipt.SameAsync(after, before);
    }
}
