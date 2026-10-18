using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueDispatchCasClusterCommands
{
    internal static Task<ModelContextProtocol.Protocol.CallToolResult> McpAsync(McpOfficialClient mcp,
        Guid id, QueueDispatchControlRequest request, CancellationToken token)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpCallerProtocol.CommandId] = id,
            [McpCallerProtocol.Request] = request
        };
        return mcp.Client.InvokeKeyLoadToolAsync(QueueDispatchControlProtocol.Tool, arguments, token).AsTask();
    }

    internal static async Task ResultAsync(ResourceDefinition actual, ResourceDefinition expected)
        => await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();

    internal static Task SuccessAsync(ClusterFixture fixture, QueueDispatchCasClusterState state, Guid id,
        long requestVersion, bool paused, long currentVersion, bool currentPaused, bool mcpFirst, CancellationToken token)
        => QueueDispatchCasClusterCallers.UseAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, async (sdk, mcp) =>
        {
            var request = state.Request(requestVersion, paused);
            var expected = state.Expected(requestVersion + QueueDispatchCasClusterProtocol.VersionStep, paused);
            if (mcpFirst)
            {
                var reply = await McpCallerAssertions.SuccessAsync<ResourceDefinition>(await McpAsync(mcp, id, request, token));
                await ResultAsync(reply.Value, expected);
            }
            else
            {
                await ResultAsync(await McpCallerAssertions.SdkSuccessAsync(await sdk.SetQueueDispatchAsync(id, request, token)), expected);
            }
            await QueueDispatchCasClusterCurrent.RunAsync(fixture, state, currentVersion, currentPaused, token);
            return true;
        }, token);

    internal static Task FailureAsync(ClusterFixture fixture, QueueDispatchCasClusterState state, Guid id,
        long expectedVersion, bool paused, ErrorCode code, long currentVersion, bool currentPaused,
        bool mcpFirst, string credential, CancellationToken token)
        => QueueDispatchCasClusterCallers.UseAsync(fixture, McpCallerProtocol.Node3, credential, async (sdk, mcp) =>
        {
            var request = state.Request(expectedVersion, paused);
            if (mcpFirst)
            {
                await McpCallerAssertions.ErrorAsync(await McpAsync(mcp, id, request, token), code, dispatched: true);
            }
            else
            {
                await QueueLeaseClusterAssertions.DeniedAsync(await sdk.SetQueueDispatchAsync(id, request, token), code);
            }
            await QueueDispatchCasClusterCurrent.RunAsync(fixture, state, currentVersion, currentPaused, token);
            return true;
        }, token);
}
