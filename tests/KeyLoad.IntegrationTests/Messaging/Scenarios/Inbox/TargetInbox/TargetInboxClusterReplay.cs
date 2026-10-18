using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class TargetInboxClusterReplay
{
    internal static async Task ExecuteAsync(KeyLoadClient client, McpOfficialClient mcp,
        TargetInboxClusterState state, bool originalAllowed, CancellationToken token)
    {
        if (originalAllowed)
        {
            await TargetInboxClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CompleteAsync(state.SourceAck, token)), state.SourceAckReceipt);
            await TargetInboxClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.MessagesComplete, state.SourceAck, token))).Value, state.SourceAckReceipt);
            await TargetInboxClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CommitInboxAsync(state.Original, token)), state.Result);
            await TargetInboxClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitInboxResult>(await mcp.CallAsync(
                TargetInboxClusterProtocol.Tool, state.Original, token))).Value, state.Result);
        }
        else
        {
            await TargetInboxClusterAssertions.DeniedAsync(await client.CommitInboxAsync(state.Original, token), ErrorCode.PermissionDenied);
            _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(TargetInboxClusterProtocol.Tool, state.Original, token), ErrorCode.PermissionDenied, true);
        }
        var duplicate = state.Original with
        {
            CommandId = Guid.NewGuid()
        };
        var expected = state.Result with
        {
            AlreadyProcessed = true
        };
        await TargetInboxClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CommitInboxAsync(duplicate, token)), expected);
        await TargetInboxClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitInboxResult>(await mcp.CallAsync(
            TargetInboxClusterProtocol.Tool, duplicate, token))).Value, expected);
        var sql = new SqlOperationRequest(state.Target.Partition, TargetInboxClusterProtocol.Sql, new(StringComparer.Ordinal)
        {
            [TargetInboxClusterProtocol.Arguments] = JsonSerializer.SerializeToElement(McpOfficialClient.Arguments(duplicate), JsonDefaults.Options)
        });
        await TargetInboxClusterAssertions.EqualAsync((await McpCallerAssertions.SdkSuccessAsync(await client.ExecuteSqlAsync(sql, token)))
            .Deserialize<CommitInboxResult>(JsonDefaults.Options)!, expected);
        await TargetInboxClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitInboxResult>(await mcp.CallAsync(
            SqlOperationProtocol.ToolName, sql, token))).Value, expected);
        var changed = duplicate with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(TargetInboxClusterProtocol.Collection, TargetInboxClusterProtocol.Changed, TargetInboxClusterProtocol.Payload)]
        };
        await TargetInboxClusterAssertions.DeniedAsync(await client.CommitInboxAsync(changed, token), ErrorCode.Conflict);
        _ = await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(TargetInboxClusterProtocol.Tool, changed, token), ErrorCode.Conflict, true);
        await TargetInboxClusterAssertions.UnchangedAsync(client, state, token);
    }
}
