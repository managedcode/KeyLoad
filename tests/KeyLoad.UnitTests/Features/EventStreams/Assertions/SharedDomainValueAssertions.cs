using System.Text.Json;
using KeyLoad.Storage;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainValueAssertions
{
    private const string PutKind = "putDocument";
    private const string AppendKind = "appendEvents";
    private const string EnqueueKind = "enqueue";

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));

    internal static async Task ReceiptAsync(OperationResult result, CommandRequest request, StoreIdentity identity, long position)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        await Assert.That(result.Json).IsNull();
        var mutations = request.Mutations.Select(effect => effect switch
        {
            PutDocument document => new MutationReceipt(PutKind, document.Collection, document.Id, FirstRevision),
            AppendEvents append => new MutationReceipt(AppendKind, append.StreamSet, append.StreamId, FirstRevision),
            EnqueueMessage enqueue => new MutationReceipt(EnqueueKind, enqueue.Queue, enqueue.MessageId, FirstRevision),
            _ => throw new InvalidOperationException(UnexpectedMutation)
        }).ToArray();
        await EqualAsync(result.Get<CommitReceipt>(), new CommitReceipt(request.CommandId,
            new(identity.Incarnation, request.Partition.AtomicPartitionId, position, Generation), [.. mutations], DurabilityProfile.ProcessDurable));
        await EmptyCompositionAsync(result.Get<CommitReceipt>());
    }

    private const string UnexpectedMutation = "The shared-domain producer has an unexpected mutation.";

    internal static async Task ProcessingAsync(OperationResult result, ProcessingRequest request, StoreIdentity identity, long position)
    {
        await Assert.That(result.Error).IsNull();
        await Assert.That(result.SafeDetail).IsNull();
        await Assert.That(result.Json).IsNull();
        var scope = request.Lane;
        var put = request.Effects.OfType<PutDocument>().Single();
        var append = request.Effects.OfType<AppendEvents>().Single();
        var enqueue = request.Effects.OfType<EnqueueMessage>().Single();
        await EqualAsync(result.Get<CommitReceipt>(), new CommitReceipt(request.CommandId,
            new(identity.Incarnation, scope.Partition.AtomicPartitionId, position, Generation),
            [new(PutKind, put.Collection, Processed, FirstRevision), new(AppendKind, append.StreamSet, Item, SecondRevision),
             new(EnqueueKind, enqueue.Queue, Processed, FirstRevision), new(nameof(DeliveryAction.Ack), scope.Queue, Item, AckVersion)],
            DurabilityProfile.ProcessDurable));
        await EmptyCompositionAsync(result.Get<CommitReceipt>());
    }

    private static async Task EmptyCompositionAsync(CommitReceipt receipt)
    {
        foreach (var mutation in receipt.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }

    internal static async Task FailureAsync(OperationResult result, ErrorCode code, string detail)
    {
        await Assert.That(result.Error).IsEqualTo(code);
        await Assert.That(result.SafeDetail).IsEqualTo(detail);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }
}
