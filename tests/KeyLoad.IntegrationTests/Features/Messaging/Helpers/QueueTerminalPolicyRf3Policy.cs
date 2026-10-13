using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueTerminalPolicyRf3Policy
{
    internal static async Task RefuseAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, CancellationToken token)
    {
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId,
            state.Definition with { SchemaVersion = 2L, QueuePolicy = state.Definition.QueuePolicy with { MaxAttempts = 1 } })
        { ExpectedSchemaVersion = 1L };
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await admin.Sdk.ConfigureResourceAsync(
            state.RefusedPolicy, request, token), ErrorCode.UnsupportedCapability);
        await QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
        await McpCallerAssertions.ErrorAsync(await InvokeAsync(admin, state.RefusedPolicy, request, token),
            ErrorCode.UnsupportedCapability, dispatched: true);
        await QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
    }

    internal static async Task VerifyCurrentAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, CancellationToken token)
    {
        await QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, state.Definition);
        var id = Guid.NewGuid();
        var result = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token));
        await QueueTerminalPolicyRf3Assertions.EqualAsync(result, state.Definition);
        state.Configurations.Add((id, request, result));
        await QueueTerminalPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await ReplayAsync(admin, id, request, result,
            () => QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers admin, Guid id, ConfigureResourceRequest request,
        ResourceDefinition original, Func<Task> current, CancellationToken token)
    {
        await QueueTerminalPolicyRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(id, request, token)), original);
        await current();
        await QueueTerminalPolicyRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ResourceDefinition>(
            await InvokeAsync(admin, id, request, token))).Value, original);
        await current();
    }

    private static Task<ModelContextProtocol.Protocol.CallToolResult> InvokeAsync(RequestCqrsRf3Callers admin, Guid id,
        ConfigureResourceRequest request, CancellationToken token)
    {
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, id);
        return admin.Mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure, arguments, cancellationToken: token).AsTask();
    }

    internal static async Task RefuseProducerAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueTerminalPolicyRf3State state, CancellationToken token)
    {
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(await worker.Sdk.CommitAsync(state.RefusedProducer, token), ErrorCode.Conflict);
        await QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
        await McpCallerAssertions.ErrorAsync(await worker.Mcp.CallAsync(McpCallerTools.DocumentsCommit,
            state.RefusedProducer, token), ErrorCode.Conflict, dispatched: true);
        await QueueTerminalPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
    }
}
