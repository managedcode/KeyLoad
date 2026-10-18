using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerClusterReplay
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, QueueProducerClusterOriginal original,
        List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await QueueProducerClusterObservedReplay.RunAsync(publisher, mcp, seed, seed.Original, original.Receipt,
                QueueProducerClusterProtocol.Original, original.Image, token);
            await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(publisher, seed,
                QueueProducerClusterProtocol.Original, token), original.Image);
            await QueueProducerClusterAssertions.PublicImageAsync(mcp, seed, QueueProducerClusterProtocol.Original, original.Image, token);
            await Assert.That(seed.Due > TimeProvider.System.GetUtcNow()).IsTrue();
        }, failures).ConfigureAwait(false);
    }
}
