using System.Text.Json;
using KeyLoad.CrashHost.Features.EventStreams;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryValues
{
    private const string PutKind = "putDocument";
    private const string AppendKind = "appendEvents";
    private const string EnqueueKind = "enqueue";
    private const string UnsupportedMutation = "The genuine shared-domain producer has an unexpected mutation.";

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));

    internal static async Task ResultAsync(OperationResult actual, OperationResult expected)
    {
        await Assert.That(actual.Json).IsEqualTo(expected.Json);
        await Assert.That(actual.Error).IsEqualTo(expected.Error);
        await Assert.That(actual.SafeDetail).IsEqualTo(expected.SafeDetail);
        await Assert.That(actual.NativeValue?.GetType()).IsEqualTo(expected.NativeValue?.GetType());
        if (expected.NativeValue is CommitReceipt expectedReceipt)
        {
            await ReceiptValueAsync(actual.Get<CommitReceipt>(), expectedReceipt);
        }
        else if (expected.NativeValue is ReceiveResult received)
        {
            await EqualAsync(actual.Get<ReceiveResult>(), received);
        }
    }

    internal static async Task ReceiptValueAsync(CommitReceipt actual, CommitReceipt expected)
        => await EqualAsync(new
        {
            Receipt = actual,
            CompositionReferences = actual.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
        }, new
        {
            Receipt = expected,
            CompositionReferences = expected.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
        });

    internal static async Task ProducerAsync(OperationResult actual, SharedDomainScope scope, Guid id, string item,
        Guid incarnation, long position)
    {
        await Assert.That(actual.Error).IsNull();
        await Assert.That(actual.SafeDetail).IsNull();
        await Assert.That(actual.Json).IsNull();
        var command = SharedDomainCorpus.Producer(scope, id, item);
        var mutations = command.Mutations.Select(mutation => mutation switch
        {
            PutDocument document => new MutationReceipt(PutKind, document.Collection, document.Id, FirstRevision),
            AppendEvents events => new MutationReceipt(AppendKind, events.StreamSet, events.StreamId, FirstRevision),
            EnqueueMessage message => new MutationReceipt(EnqueueKind, message.Queue, message.MessageId, FirstRevision),
            _ => throw new InvalidOperationException(UnsupportedMutation)
        }).ToArray();
        var expected = new CommitReceipt(id, new(incarnation, scope.Partition.AtomicPartitionId, position, Generation),
            [.. mutations], DurabilityProfile.ProcessDurable);
        await ReceiptValueAsync(actual.Get<CommitReceipt>(), expected);
    }
}
