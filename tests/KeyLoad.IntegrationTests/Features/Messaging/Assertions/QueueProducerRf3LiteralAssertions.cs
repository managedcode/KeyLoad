using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3LiteralAssertions
{
    internal static async Task ProducerAsync(ClusterFixture fixture, QueueProducerRf3Seed seed, CommandRequest command,
        CommitReceipt receipt, QueueProducerRf3Image image, string id, string payload, long sequence, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var placement = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(new(QueueProducerRf3EnqueueProtocol.PlacementVersion, command.Partition), token));
        await Assert.That(placement.Partition).IsEqualTo(command.Partition);
        await Assert.That(receipt.Token.Position).IsGreaterThan(QueueProducerRf3EnqueueProtocol.NoPosition);
        var mutations = command.Mutations.Select(mutation => mutation switch
        {
            PutDocument document => new MutationReceipt(QueueProducerRf3EnqueueProtocol.PutKind, document.Collection, document.Id, QueueProducerRf3Protocol.InitialRevision),
            AppendEvents append => new MutationReceipt(QueueProducerRf3EnqueueProtocol.AppendKind, append.StreamSet, append.StreamId, QueueProducerRf3Protocol.InitialRevision),
            EnqueueMessage message => new MutationReceipt(QueueProducerRf3EnqueueProtocol.EnqueueKind, message.Queue, message.MessageId, QueueProducerRf3Protocol.InitialRevision),
            _ => throw new InvalidOperationException(QueueProducerRf3EnqueueProtocol.UnexpectedMutation)
        }).ToImmutableArray();
        await QueueProducerRf3Assertions.EqualAsync(receipt, new CommitReceipt(command.CommandId,
            new(placement.Incarnation, command.Partition.AtomicPartitionId, receipt.Token.Position, placement.PlacementEpoch),
            mutations, DurabilityProfile.QuorumProcessDurable));
        await QueueProducerRf3Assertions.EqualAsync(image.Ready,
            new MessageInspection(new(id, MessageState.Ready, QueueProducerRf3Protocol.InitialAttempts, QueueProducerRf3Protocol.InitialRevision, sequence, null, null), payload, QueueProducerRf3Protocol.Headers));
        await QueueProducerRf3Assertions.EqualAsync(image.Scheduled,
            new MessageInspection(new(QueueProducerRf3Protocol.Scheduled, MessageState.Scheduled, QueueProducerRf3Protocol.InitialAttempts, QueueProducerRf3Protocol.InitialRevision, QueueProducerRf3Protocol.ScheduledReadySequence, seed.Due, null),
                QueueProducerRf3Protocol.Payload, QueueProducerRf3Protocol.Headers));
        await Assert.That(image.Document.Reference).IsEqualTo(QueueProducerRf3Assertions.Document(seed, id));
        await Assert.That(image.Document.Json).IsEqualTo(payload);
        await Assert.That(image.Document.Revision).IsEqualTo(QueueProducerRf3Protocol.InitialRevision);
        await Assert.That(image.Document.Redacted).IsFalse();
        await Assert.That(image.Document.RedactedFields).IsEmpty();
        await Assert.That(seed.Due > TimeProvider.System.GetUtcNow()).IsTrue();
    }

    internal static async Task UnchangedAsync(KeyLoadClient client, QueueProducerRf3Seed seed,
        QueueProducerRf3Image original, CancellationToken token)
    {
        await QueueProducerRf3Assertions.EqualAsync(await QueueProducerRf3Assertions.ImageAsync(client, seed,
            QueueProducerRf3Protocol.Original, token), original);
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await client.GetAsync(
            QueueProducerRf3Assertions.Document(seed, QueueProducerRf3Protocol.Refused), token))).IsNull();
        await AtomicProducerRf3EventAssertions.AbsentAsync(client, seed, QueueProducerRf3Protocol.Refused, token);
    }
}
