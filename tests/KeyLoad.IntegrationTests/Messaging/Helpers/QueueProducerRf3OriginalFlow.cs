using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3OriginalFlow
{
    internal static async Task<QueueProducerRf3Original?> ExecuteAsync(ClusterFixture fixture,
        QueueProducerRf3Seed seed, bool cancelAfterResponse, List<Exception> failures, CancellationToken token)
    {
        var returned = await QueueProducerRf3ResponseCall.SendAsync(fixture, seed, cancelAfterResponse, failures, token);
        if (failures.Count != QueueProducerRf3Protocol.NoFailures)
        {
            return null;
        }
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        QueueProducerRf3Original? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var receipt = (await McpCallerAssertions.SuccessAsync<CommitReceipt>(await mcp.CallAsync(
                McpCallerTools.DocumentsCommit, seed.Original, token))).Value;
            await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
            await Assert.That(receipt.CommandId).IsEqualTo(seed.Original.CommandId);
            await Assert.That(receipt.Mutations.Length).IsEqualTo(QueueProducerRf3Protocol.OriginalMutationCount);
            if (returned is not null)
            {
                await QueueProducerRf3Assertions.EqualAsync(returned, receipt);
            }
            var image = await QueueProducerRf3Assertions.ImageAsync(publisher, seed, QueueProducerRf3Protocol.Original, token);
            await QueueProducerRf3ObservedReplay.RunAsync(publisher, mcp, seed, seed.Original, receipt,
                QueueProducerRf3Protocol.Original, image, token);
            await AtomicProducerRf3EventAssertions.LiteralAsync(image.Events, seed, QueueProducerRf3Protocol.Original,
                QueueProducerRf3Protocol.Payload, QueueProducerRf3Protocol.OriginalEventSequence);
            await QueueProducerRf3LiteralAssertions.ProducerAsync(fixture, seed, seed.Original, receipt, image,
                QueueProducerRf3Protocol.Original, QueueProducerRf3Protocol.Payload, QueueProducerRf3Protocol.InitialReadySequence, token);
            await QueueProducerRf3Assertions.PublicImageAsync(mcp, seed, QueueProducerRf3Protocol.Original, image, token);
            result = new(seed, receipt, image);
        }, failures).ConfigureAwait(false);
        return result;
    }
}
