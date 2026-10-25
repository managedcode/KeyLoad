using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueTerminalPolicyClusterPolicy
{
    internal static async Task RefuseAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, CancellationToken token)
    {
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId,
            state.Definition with
            {
                SchemaVersion = 2L,
                QueuePolicy = state.Definition.QueuePolicy with
                {
                    MaxAttempts = 1
                }
            })
        {
            ExpectedSchemaVersion = 1L
        };
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await admin.Sdk.ConfigureResourceAsync(
            state.RefusedPolicy, request, token), ErrorCode.UnsupportedCapability);
        await QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        _ = (await McpCallerAssertions.ErrorAsync(await InvokeAsync(admin, state.RefusedPolicy, request, token),
            ErrorCode.UnsupportedCapability, dispatched: true)).Value;
        await QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
    }

    internal static async Task VerifyCurrentAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, CancellationToken token)
    {
        await QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, state.Definition);
        var id = Guid.NewGuid();
        var result = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token));
        await QueueTerminalPolicyClusterAssertions.EqualAsync(result, state.Definition);
        state.Configurations.Add((id, request, result));
        await QueueTerminalPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await ReplayAsync(admin, id, request, result,
            () => QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, Guid id, ConfigureResourceRequest request,
        ResourceDefinition original, Func<Task> current, CancellationToken token)
    {
        await QueueTerminalPolicyClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(id, request, token)), original);
        await current();
        await QueueTerminalPolicyClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ResourceDefinition>(
            await InvokeAsync(admin, id, request, token))).Value, original);
        await current();
    }

    private static Task<ModelContextProtocol.Protocol.CallToolResult> InvokeAsync(RequestCqrsClusterCallers admin, Guid id,
        ConfigureResourceRequest request, CancellationToken token)
    {
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, id);
        return admin.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure, arguments, cancellationToken: token).AsTask();
    }

    internal static async Task RefuseProducerAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueTerminalPolicyClusterState state, CancellationToken token)
    {
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(await worker.Sdk.CommitAsync(state.RefusedProducer, token), ErrorCode.Conflict);
        await QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        _ = (await McpCallerAssertions.ErrorAsync(await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            state.RefusedProducer, token), ErrorCode.Conflict, dispatched: true)).Value;
        await QueueTerminalPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
    }
}
