using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
namespace KeyLoad.ClusterTests.Messaging;

internal static class InboxProcessingClusterReplay
{
    internal static async Task ExecuteAsync(KeyLoadClient client, McpOfficialClient mcp,
        InboxProcessingClusterState state, bool originalAllowed, CancellationToken token)
    {
        if (originalAllowed)
        {
            await InboxProcessingClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await client.CommitProcessingAsync(state.Original, token)), state.Receipt);
            await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.MessagesProcess, state.Original, token))).Value, state.Receipt);
        }
        else
        {
            await InboxProcessingClusterAssertions.DeniedAsync(await client.CommitProcessingAsync(state.Original, token), ErrorCode.PermissionDenied);
            _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess,
                state.Original, token), ErrorCode.PermissionDenied, true)).Value;
        }
        var duplicate = state.Original with
        {
            CommandId = Guid.NewGuid()
        };
        await InboxProcessingClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.MessagesProcess, duplicate, token))).Value, state.Receipt);
        var changed = state.Original with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(InboxProcessingClusterProtocol.Collection, InboxProcessingClusterProtocol.Refused, InboxProcessingClusterProtocol.Payload)]
        };
        await InboxProcessingClusterAssertions.DeniedAsync(await client.CommitProcessingAsync(changed, token), ErrorCode.Conflict);
        _ = (await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess,
            changed, token), ErrorCode.Conflict, true)).Value;
        await InboxProcessingClusterAssertions.UnchangedAsync(client, state, token);
    }
}
