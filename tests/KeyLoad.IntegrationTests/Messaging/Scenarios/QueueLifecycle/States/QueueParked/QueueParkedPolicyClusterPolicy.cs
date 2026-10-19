using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueParkedPolicyClusterPolicy
{
    internal static async Task RefuseAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        var sdk = await admin.Sdk.ConfigureResourceAsync(state.RefusedPolicy, state.Replacement, token);
        await SagaTimeoutClusterAssertions.AssertSdkErrorAsync(sdk, ErrorCode.Conflict);
        await Assert.That(sdk.Value).IsNull();
        var problem = System.Text.Json.JsonSerializer.SerializeToElement(sdk.Problem, JsonDefaults.Options);
        if (state.RefusedProblem is { } original)
        {
            await QueueParkedPolicyClusterAssertions.EqualAsync(problem, original);
        }
        else
        {
            state.RefusedProblem = problem;
        }
        await QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        var mcp = await InvokeAsync(admin, state.RefusedPolicy, state.Replacement, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Conflict, dispatched: true);
        var error = (mcp.StructuredContent ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult)).GetProperty(McpCallerProtocol.Error).Clone();
        if (state.RefusedMcpError is { } originalError)
        {
            await QueueParkedPolicyClusterAssertions.EqualAsync(error, originalError);
        }
        else
        {
            state.RefusedMcpError = error;
        }
        await QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
    }

    internal static async Task ChangeAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var result = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, state.Replacement, token));
        await QueueParkedPolicyClusterAssertions.EqualAsync(result, state.Replacement.Definition);
        state.Definition = state.Replacement.Definition;
        state.Configurations.Add((id, state.Replacement, result));
        await QueueParkedPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await ReplayAsync(admin, id, state.Replacement, result,
            () => QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
        await VerifyCurrentAsync(admin, worker, state, token);
    }

    internal static async Task VerifyCurrentAsync(RequestCqrsClusterCallers admin, RequestCqrsClusterCallers worker,
        QueueParkedPolicyClusterState state, CancellationToken token)
    {
        await QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token);
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, state.Definition);
        var id = Guid.NewGuid();
        var result = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token));
        await QueueParkedPolicyClusterAssertions.EqualAsync(result, state.Definition);
        state.Configurations.Add((id, request, result));
        await QueueParkedPolicyClusterAssertions.CaptureAsync(admin, worker, state, token);
        await ReplayAsync(admin, id, request, result,
            () => QueueParkedPolicyClusterAssertions.CurrentAsync(admin, worker, state, token), token);
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers admin, Guid id, ConfigureResourceRequest request,
        ResourceDefinition original, Func<Task> current, CancellationToken token)
    {
        await QueueParkedPolicyClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(id, request, token)), original);
        await current();
        await QueueParkedPolicyClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ResourceDefinition>(
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
}
