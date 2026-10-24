using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueWorkerClusterHealthyImages
{
    internal static async Task<MessageInspection?[]> CaptureAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterSeed seed, CancellationToken token)
    {
        var retained = new List<MessageInspection?>();
        foreach (var id in QueueWorkerClusterProtocol.Messages)
        {
            retained.Add(await QueueWorkerClusterAssertions.ImageAsync(sdk, mcp, seed.Lane, id, token));
        }
        return retained.ToArray();
    }

    internal static async Task RetainedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerClusterSeed seed, MessageInspection?[] original, CancellationToken token)
    {
        var current = await CaptureAsync(sdk, mcp, seed, token);
        await QueueWorkerClusterAssertions.EqualAsync(current, original);
    }
}
