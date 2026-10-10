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
        { return null; }
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
            { await QueueProducerRf3Assertions.EqualAsync(returned, receipt); }
            await QueueProducerRf3Assertions.ReplayAsync(publisher, mcp, seed.Original, receipt, token);
            var image = await QueueProducerRf3Assertions.ImageAsync(publisher, seed, QueueProducerRf3Protocol.Original, token);
            await AtomicProducerRf3EventAssertions.LiteralAsync(image.Events, seed, QueueProducerRf3Protocol.Original,
                QueueProducerRf3Protocol.Payload, QueueProducerRf3Protocol.OriginalEventSequence);
            await Assert.That(image.Document.Json).IsEqualTo(QueueProducerRf3Protocol.Payload);
            await Assert.That(image.Document.Revision).IsEqualTo(QueueProducerRf3Protocol.InitialRevision);
            await Assert.That(image.Document.Reference).IsEqualTo(QueueProducerRf3Assertions.Document(seed, QueueProducerRf3Protocol.Original));
            await Assert.That(image.Document.Redacted).IsFalse();
            await Assert.That(image.Document.RedactedFields).IsEmpty();
            await Assert.That(image.Ready.Metadata.Id).IsEqualTo(QueueProducerRf3Protocol.Original);
            await Assert.That(image.Scheduled.Metadata.Id).IsEqualTo(QueueProducerRf3Protocol.Scheduled);
            await Assert.That(image.Ready.Metadata.ReadySequence).IsEqualTo(QueueProducerRf3Protocol.InitialReadySequence);
            await Assert.That(image.Scheduled.Metadata.ReadySequence).IsEqualTo(QueueProducerRf3Protocol.ScheduledReadySequence);
            await Assert.That(image.Ready.Metadata.State).IsEqualTo(MessageState.Ready);
            await Assert.That(image.Scheduled.Metadata.State).IsEqualTo(MessageState.Scheduled);
            await Assert.That(image.Scheduled.Metadata.NotBefore).IsEqualTo(seed.Due);
            await Assert.That(seed.Due > TimeProvider.System.GetUtcNow()).IsTrue();
            foreach (var message in new[] { image.Ready, image.Scheduled })
            {
                await Assert.That(message.PayloadJson).IsEqualTo(QueueProducerRf3Protocol.Payload);
                await Assert.That(message.HeadersJson).IsEqualTo(QueueProducerRf3Protocol.Headers);
                await Assert.That(message.Metadata.LeaseOwner).IsNull();
                await Assert.That(message.Metadata.LeaseUntil).IsNull();
                await Assert.That(message.Metadata.ExpiresAt).IsNull();
                await Assert.That(message.Metadata.Attempts).IsEqualTo(QueueProducerRf3Protocol.InitialAttempts);
                await Assert.That(message.Metadata.LeaseVersion).IsEqualTo(QueueProducerRf3Protocol.UnclaimedLeaseVersion);
                await Assert.That(message.Metadata.DeliveryGeneration).IsEqualTo(QueueProducerRf3Protocol.InitialGeneration);
            }
            await QueueProducerRf3Assertions.PublicImageAsync(mcp, seed, QueueProducerRf3Protocol.Original, image, token);
            result = new(seed, receipt, image);
        }, failures).ConfigureAwait(false);
        return result;
    }
}
