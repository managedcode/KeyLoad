using System.Text.Json;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;
using static KeyLoad.IntegrationTests.Features.EventStreams.SharedDomainPublicProtocol;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class SharedDomainPublicValues
{
    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));

    internal static async Task ProducerAsync(CommandRequest command, CommitReceipt receipt)
    {
        var mutations = command.Mutations.Select(effect => effect switch
        {
            PutDocument put => new MutationReceipt(PutKind, put.Collection, put.Id, FirstRevision),
            AppendEvents append => new MutationReceipt(AppendKind, append.StreamSet, append.StreamId, FirstRevision),
            EnqueueMessage enqueue => new MutationReceipt(EnqueueKind, enqueue.Queue, enqueue.MessageId, FirstRevision),
            _ => throw new InvalidOperationException(UnexpectedMutation)
        });
        await ReceiptAsync(command.Partition, command.CommandId, receipt, mutations);
    }

    internal static async Task ProcessingAsync(SharedDomainPublicState state, ProcessingRequest request, CommitReceipt receipt)
    {
        var scope = state.Scopes[FirstIndex];
        await ReceiptAsync(scope.Partition, request.CommandId, receipt,
            [new(PutKind, scope.Documents!, Processed, FirstRevision),
             new(AppendKind, scope.Streams!, Item, SecondRevision),
             new(EnqueueKind, scope.Queue!, Processed, FirstRevision),
             new(nameof(DeliveryAction.Ack), scope.Queue!, Item, AckVersion)]);
    }

    private static async Task ReceiptAsync(PartitionRef partition, Guid id,
        CommitReceipt receipt, IEnumerable<MutationReceipt> mutations)
    {
        await Assert.That(receipt.Token.Position).IsGreaterThan(NoRevision);
        await EqualAsync(receipt, new CommitReceipt(id, new(receipt.Token.Incarnation, partition.AtomicPartitionId,
            receipt.Token.Position), [.. mutations], DurabilityProfile.ProcessDurable, 3));
    }
}
