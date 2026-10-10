using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class InboxProcessingRf3Replay
{
    internal static async Task ExecuteAsync(KeyLoadClient client, McpOfficialClient mcp,
        InboxProcessingRf3State state, bool originalAllowed, CancellationToken token)
    {
        if (originalAllowed)
        {
            await InboxProcessingRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(
                await client.CommitProcessingAsync(state.Original, token)), state.Receipt);
            await InboxProcessingRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
                await mcp.CallAsync(McpCallerTools.MessagesProcess, state.Original, token))).Value, state.Receipt);
        }
        else
        {
            await InboxProcessingRf3Assertions.DeniedAsync(await client.CommitProcessingAsync(state.Original, token), ErrorCode.PermissionDenied);
            _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess,
                state.Original, token), ErrorCode.PermissionDenied, true);
        }
        var duplicate = state.Original with { CommandId = Guid.NewGuid() };
        await InboxProcessingRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(
            await mcp.CallAsync(McpCallerTools.MessagesProcess, duplicate, token))).Value, state.Receipt);
        var changed = state.Original with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(InboxProcessingRf3Protocol.Collection, InboxProcessingRf3Protocol.Refused, InboxProcessingRf3Protocol.Payload)]
        };
        await InboxProcessingRf3Assertions.DeniedAsync(await client.CommitProcessingAsync(changed, token), ErrorCode.Conflict);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.MessagesProcess,
            changed, token), ErrorCode.Conflict, true);
        await InboxProcessingRf3Assertions.UnchangedAsync(client, state, token);
    }
}
