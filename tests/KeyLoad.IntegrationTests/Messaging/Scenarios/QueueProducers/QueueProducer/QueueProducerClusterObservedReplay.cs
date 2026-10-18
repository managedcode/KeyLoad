using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerClusterObservedReplay
{
    internal static async Task RunAsync(KeyLoadClient client, McpOfficialClient mcp, QueueProducerClusterSeed seed,
        CommandRequest command, CommitReceipt receipt, string id, QueueProducerClusterImage image, CancellationToken token)
    {
        await QueueProducerClusterAssertions.EqualAsync(await McpCallerAssertions.SdkSuccessAsync(await client.CommitAsync(command, token)), receipt);
        await CurrentAsync(client, mcp, seed, id, image, token);
        var sql = SqlClusterProtocol.Call(command.Partition, McpCallerTools.DocumentsCommit, command);
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.SdkAsync<CommitReceipt>(client, sql, token), receipt);
        await CurrentAsync(client, mcp, seed, id, image, token);
        await QueueProducerClusterAssertions.EqualAsync(await SqlClusterProtocol.McpAsync<CommitReceipt>(mcp, sql, token), receipt);
        await CurrentAsync(client, mcp, seed, id, image, token);
        await QueueProducerClusterAssertions.EqualAsync((await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
            McpCallerTools.DocumentsCommit, command, token))).Value, receipt);
        await CurrentAsync(client, mcp, seed, id, image, token);
    }

    private static async Task CurrentAsync(KeyLoadClient client, McpOfficialClient mcp, QueueProducerClusterSeed seed,
        string id, QueueProducerClusterImage image, CancellationToken token)
    {
        await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(client, seed, id, token), image);
        await QueueProducerClusterAssertions.PublicImageAsync(mcp, seed, id, image, token);
    }
}
