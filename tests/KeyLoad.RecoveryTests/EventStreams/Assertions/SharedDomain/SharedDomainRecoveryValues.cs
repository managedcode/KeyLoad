using System.Text.Json;
using ManagedCode.Communication;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryValues
{
    private const string PutKind = "putDocument";
    private const string AppendKind = "appendEvents";
    private const string EnqueueKind = "enqueue";
    private const string UnsupportedMutation = "The genuine shared-domain producer has an unexpected mutation.";

    internal static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(JsonSerializer.Serialize(actual, JsonDefaults.Options)).IsEqualTo(JsonSerializer.Serialize(expected, JsonDefaults.Options));

    internal static async Task ResultAsync(Result<object> actual, Result<object> expected)
    {
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(Errors.Code(expected.Problem));
        await Assert.That(actual.Problem?.Detail).IsEqualTo(expected.Problem?.Detail);
        await Assert.That(actual.Value?.GetType()).IsEqualTo(expected.Value?.GetType());
        if (expected.Value is CommitReceipt expectedReceipt)
        {
            await ReceiptValueAsync(actual.Get<CommitReceipt>(), expectedReceipt);
        }
        else if (expected.Value is ReceiveResult received)
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

    internal static async Task ProducerAsync(Result<object> actual, SharedDomainScope scope, Guid id, string item,
        Guid incarnation, long position)
    {
        await Assert.That(Errors.Code(actual.Problem)).IsNull();
        await Assert.That(actual.Problem?.Detail).IsNull();

        var command = SharedDomainCorpus.Producer(scope, id, item);
        var mutations = command.Mutations.Select(mutation => mutation switch
        {
            PutDocument document => new MutationReceipt(PutKind, document.Collection, document.Id, FirstRevision),
            AppendEvents events => new MutationReceipt(AppendKind, events.StreamSet, events.StreamId, FirstRevision),
            EnqueueMessage message => new MutationReceipt(EnqueueKind, message.Queue, message.MessageId, FirstRevision),
            _ => throw new InvalidOperationException(UnsupportedMutation)
        }).ToArray();
        var expected = new CommitReceipt(id, new(incarnation, scope.Partition.AtomicPartitionId, position), [.. mutations], DurabilityProfile.ProcessDurable, 1);
        await ReceiptValueAsync(actual.Get<CommitReceipt>(), expected);
    }
}
