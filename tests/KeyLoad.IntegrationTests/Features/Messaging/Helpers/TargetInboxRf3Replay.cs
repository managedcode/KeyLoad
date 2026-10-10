using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxRf3Replay
{
    internal static async Task ExecuteAsync(KeyLoadClient client, McpOfficialClient mcp,
        TargetInboxRf3State state, bool originalAllowed, CancellationToken token)
    {
        if (originalAllowed)
        {
            await TargetInboxRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CompleteAsync(state.SourceAck, token)), state.SourceAckReceipt);
            await TargetInboxRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, state.SourceAck, token))).Value, state.SourceAckReceipt);
            await TargetInboxRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CommitInboxAsync(state.Original, token)), state.Result);
            await TargetInboxRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitInboxResult>(await mcp.CallAsync(
                TargetInboxRf3Protocol.Tool, state.Original, token))).Value, state.Result);
        }
        else
        {
            await TargetInboxRf3Assertions.DeniedAsync(await client.CommitInboxAsync(state.Original, token), ErrorCode.PermissionDenied);
            _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(TargetInboxRf3Protocol.Tool, state.Original, token), ErrorCode.PermissionDenied, true);
        }
        var duplicate = state.Original with { CommandId = Guid.NewGuid() };
        var expected = state.Result with { AlreadyProcessed = true };
        await TargetInboxRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CommitInboxAsync(duplicate, token)), expected);
        await TargetInboxRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitInboxResult>(await mcp.CallAsync(
            TargetInboxRf3Protocol.Tool, duplicate, token))).Value, expected);
        var sql = new SqlOperationRequest(state.Target.Partition, TargetInboxRf3Protocol.Sql, new(StringComparer.Ordinal)
        { [TargetInboxRf3Protocol.Arguments] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(duplicate), JsonDefaults.Options) });
        await TargetInboxRf3Assertions.EqualAsync((await McpCallerAssertions.SdkSuccessAsync(await client.ExecuteSqlAsync(sql, token)))
            .Deserialize<CommitInboxResult>(JsonDefaults.Options)!, expected);
        await TargetInboxRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitInboxResult>(await mcp.CallAsync(
            SqlOperationProtocol.ToolName, sql, token))).Value, expected);
        var changed = duplicate with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(TargetInboxRf3Protocol.Collection, TargetInboxRf3Protocol.Changed, TargetInboxRf3Protocol.Payload)]
        };
        await TargetInboxRf3Assertions.DeniedAsync(await client.CommitInboxAsync(changed, token), ErrorCode.Conflict);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(TargetInboxRf3Protocol.Tool, changed, token), ErrorCode.Conflict, true);
        await TargetInboxRf3Assertions.UnchangedAsync(client, state, token);
    }
}
