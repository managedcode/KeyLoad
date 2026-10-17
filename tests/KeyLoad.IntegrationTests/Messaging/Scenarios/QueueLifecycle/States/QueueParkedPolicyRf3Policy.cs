using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueParkedPolicyRf3Policy
{
    internal static async Task RefuseAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, CancellationToken token)
    {
        var sdk = await admin.Sdk.ConfigureResourceAsync(state.RefusedPolicy, state.Replacement, token);
        await SagaTimeoutRf3Assertions.AssertSdkErrorAsync(sdk, ErrorCode.Conflict);
        await Assert.That(sdk.Value).IsNull();
        var problem = System.Text.Json.JsonSerializer.SerializeToElement(sdk.Problem, JsonDefaults.Options);
        if (state.RefusedProblem is { } original)
        {
            await QueueParkedPolicyRf3Assertions.EqualAsync(problem, original);
        }
        else
        {
            state.RefusedProblem = problem;
        }
        await QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
        var mcp = await InvokeAsync(admin, state.RefusedPolicy, state.Replacement, token);
        await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.Conflict, dispatched: true);
        var error = (mcp.StructuredContent ?? throw new InvalidOperationException(McpCallerProtocol.MissingStructuredResult)).GetProperty(McpCallerProtocol.Error).Clone();
        if (state.RefusedMcpError is { } originalError)
        {
            await QueueParkedPolicyRf3Assertions.EqualAsync(error, originalError);
        }
        else
        {
            state.RefusedMcpError = error;
        }
        await QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
    }

    internal static async Task ChangeAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var result = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, state.Replacement, token));
        await QueueParkedPolicyRf3Assertions.EqualAsync(result, state.Replacement.Definition);
        state.Definition = state.Replacement.Definition;
        state.Configurations.Add((id, state.Replacement, result));
        await QueueParkedPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await ReplayAsync(admin, id, state.Replacement, result,
            () => QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
        await VerifyCurrentAsync(admin, worker, state, token);
    }

    internal static async Task VerifyCurrentAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers worker,
        QueueParkedPolicyRf3State state, CancellationToken token)
    {
        await QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token);
        var request = new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, state.Definition);
        var id = Guid.NewGuid();
        var result = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ConfigureResourceAsync(id, request, token));
        await QueueParkedPolicyRf3Assertions.EqualAsync(result, state.Definition);
        state.Configurations.Add((id, request, result));
        await QueueParkedPolicyRf3Assertions.CaptureAsync(admin, worker, state, token);
        await ReplayAsync(admin, id, request, result,
            () => QueueParkedPolicyRf3Assertions.CurrentAsync(admin, worker, state, token), token);
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers admin, Guid id, ConfigureResourceRequest request,
        ResourceDefinition original, Func<Task> current, CancellationToken token)
    {
        await QueueParkedPolicyRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
            await admin.Sdk.ConfigureResourceAsync(id, request, token)), original);
        await current();
        await QueueParkedPolicyRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<ResourceDefinition>(
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
}
