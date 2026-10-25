using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Messaging;

internal static class QueueProducerClusterLiteralAssertions
{
    internal static async Task ProducerAsync(ClusterFixture fixture, QueueProducerClusterSeed seed, CommandRequest command,
        CommitReceipt receipt, QueueProducerClusterImage image, string id, string payload, long sequence)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        await Assert.That(receipt.Token.Position).IsGreaterThan(QueueProducerClusterEnqueueProtocol.NoPosition);
        var mutations = command.Mutations.Select(mutation => mutation switch
        {
            PutDocument document => new MutationReceipt(QueueProducerClusterEnqueueProtocol.PutKind, document.Collection, document.Id, QueueProducerClusterProtocol.InitialRevision),
            AppendEvents append => new MutationReceipt(QueueProducerClusterEnqueueProtocol.AppendKind, append.StreamSet, append.StreamId, QueueProducerClusterProtocol.InitialRevision),
            EnqueueMessage message => new MutationReceipt(QueueProducerClusterEnqueueProtocol.EnqueueKind, message.Queue, message.MessageId, QueueProducerClusterProtocol.InitialRevision),
            _ => throw new InvalidOperationException(QueueProducerClusterEnqueueProtocol.UnexpectedMutation)
        }).ToImmutableArray();
        await QueueProducerClusterAssertions.EqualAsync(receipt, new CommitReceipt(command.CommandId,
            new(receipt.Token.Incarnation, command.Partition.AtomicPartitionId, receipt.Token.Position),
            mutations, DurabilityProfile.ProcessDurable, 3));
        await QueueProducerClusterAssertions.EqualAsync(image.Ready,
            new MessageInspection(new(id, MessageState.Ready, QueueProducerClusterProtocol.InitialAttempts, QueueProducerClusterProtocol.InitialRevision, sequence, null, null), payload, QueueProducerClusterProtocol.Headers));
        await QueueProducerClusterAssertions.EqualAsync(image.Scheduled,
            new MessageInspection(new(QueueProducerClusterProtocol.Scheduled, MessageState.Scheduled, QueueProducerClusterProtocol.InitialAttempts, QueueProducerClusterProtocol.InitialRevision, QueueProducerClusterProtocol.ScheduledReadySequence, seed.Due, null),
                QueueProducerClusterProtocol.Payload, QueueProducerClusterProtocol.Headers));
        await Assert.That(image.Document.Reference).IsEqualTo(QueueProducerClusterAssertions.StoredDocument(seed, id));
        await Assert.That(image.Document.Json).IsEqualTo(payload);
        await Assert.That(image.Document.Revision).IsEqualTo(QueueProducerClusterProtocol.InitialRevision);
        await Assert.That(image.Document.Redacted).IsFalse();
        await Assert.That(image.Document.RedactedFields).IsEmpty();
        await Assert.That(seed.Due > TimeProvider.System.GetUtcNow()).IsTrue();
    }

    internal static async Task UnchangedAsync(KeyLoadClient client, QueueProducerClusterSeed seed,
        QueueProducerClusterImage original, CancellationToken token)
    {
        await QueueProducerClusterAssertions.EqualAsync(await QueueProducerClusterAssertions.ImageAsync(client, seed,
            QueueProducerClusterProtocol.Original, token), original);
        await Assert.That(await PublicReadAssertions.DocumentAsync(await client.GetAsync(
            QueueProducerClusterAssertions.Document(seed, QueueProducerClusterProtocol.Refused), token))).IsNull();
        await AtomicProducerClusterEventAssertions.AbsentAsync(client, seed, QueueProducerClusterProtocol.Refused, token);
    }
}
