using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueWorkerRf3HealthyImages
{
    internal static async Task<MessageInspection?[]> CaptureAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3Seed seed, CancellationToken token)
    {
        var retained = new List<MessageInspection?>();
        foreach (var id in QueueWorkerRf3Protocol.Messages)
        {
            retained.Add(await QueueWorkerRf3Assertions.ImageAsync(sdk, mcp, seed.Lane, id, token));
        }
        return retained.ToArray();
    }

    internal static async Task RetainedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        QueueWorkerRf3Seed seed, MessageInspection?[] original, CancellationToken token)
    {
        var current = await CaptureAsync(sdk, mcp, seed, token);
        await QueueWorkerRf3Assertions.EqualAsync(current, original);
    }
}
