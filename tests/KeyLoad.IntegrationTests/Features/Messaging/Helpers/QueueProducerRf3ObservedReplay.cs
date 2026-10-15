using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3ObservedReplay
{
    internal static async Task RunAsync(KeyLoadClient client, McpOfficialClient mcp, QueueProducerRf3Seed seed,
        CommandRequest command, CommitReceipt receipt, string id, QueueProducerRf3Image image, CancellationToken token)
    {
        await QueueProducerRf3Assertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(command, token)), receipt);
        await CurrentAsync(client, mcp, seed, id, image, token);
        var sql = SqlRf3Protocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.SdkAsync<CommitReceipt>(client, sql, token), receipt);
        await CurrentAsync(client, mcp, seed, id, image, token);
        await QueueProducerRf3Assertions.EqualAsync(await SqlRf3Protocol.McpAsync<CommitReceipt>(mcp, sql, token), receipt);
        await CurrentAsync(client, mcp, seed, id, image, token);
        await QueueProducerRf3Assertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
        await CurrentAsync(client, mcp, seed, id, image, token);
    }

    private static async Task CurrentAsync(KeyLoadClient client, McpOfficialClient mcp, QueueProducerRf3Seed seed,
        string id, QueueProducerRf3Image image, CancellationToken token)
    {
        await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(client, seed, id, token), image);
        await QueueProducerRf3Assertions.PublicImageAsync(mcp, seed, id, image, token);
    }
}
