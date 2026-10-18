using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerClusterOriginalFlow
{
    internal static async Task<QueueProducerClusterOriginal?> ExecuteAsync(ClusterFixture fixture,
        QueueProducerClusterSeed seed, bool cancelAfterResponse, List<Exception> failures, CancellationToken token)
    {
        var returned = await QueueProducerClusterResponseCall.SendAsync(fixture, seed, cancelAfterResponse, failures, token);
        if (failures.Count != QueueProducerClusterProtocol.NoFailures)
        {
            return null;
        }
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        QueueProducerClusterOriginal? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.DocumentsCommit, seed.Original, token))).Value;
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
            await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
            await Assert.That(receipt.CommandId).IsEqualTo(seed.Original.CommandId);
            await Assert.That(receipt.Mutations.Length).IsEqualTo(QueueProducerClusterProtocol.OriginalMutationCount);
            if (returned is not null)
            {
                await QueueProducerClusterAssertions.EqualAsync(returned, receipt);
            }
            var image = await QueueProducerClusterAssertions.ImageAsync(publisher, seed, QueueProducerClusterProtocol.Original, token);
            await QueueProducerClusterObservedReplay.RunAsync(publisher, mcp, seed, seed.Original, receipt,
                QueueProducerClusterProtocol.Original, image, token);
            await AtomicProducerClusterEventAssertions.LiteralAsync(image.Events, seed, QueueProducerClusterProtocol.Original,
                QueueProducerClusterProtocol.Payload, QueueProducerClusterProtocol.OriginalEventSequence);
            await QueueProducerClusterLiteralAssertions.ProducerAsync(fixture, seed, seed.Original, receipt, image,
                QueueProducerClusterProtocol.Original, QueueProducerClusterProtocol.Payload, QueueProducerClusterProtocol.InitialReadySequence);
            await QueueProducerClusterAssertions.PublicImageAsync(mcp, seed, QueueProducerClusterProtocol.Original, image, token);
            result = new(seed, receipt, image);
        }, failures).ConfigureAwait(false);
        return result;
    }
}
