using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3Replay
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, QueueProducerRf3Original original,
        List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueProducerRf3Assertions.ReplayAsync(publisher, mcp, seed.Original, original.Receipt, token);
            await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(publisher, seed,
                QueueProducerRf3Protocol.Original, token), original.Image);
            await QueueProducerRf3Assertions.PublicImageAsync(mcp, seed, QueueProducerRf3Protocol.Original, original.Image, token);
            await Assert.That(seed.Due > TimeProvider.System.GetUtcNow()).IsTrue();
        }, failures).ConfigureAwait(false);
    }
}
