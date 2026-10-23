using KeyLoad.Storage;
using static KeyLoad.RecoveryTests.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainInboxAssertions
{
    private const string Inbox = "inbox";

    internal static async Task RequireAsync(IAtomicStore store, ProcessingRequest request, CommitReceipt receipt)
    {
        var prefix = KeySpace.Partition(Inbox, request.Lane.Partition, request.Lane.Queue);
        var page = store.Read(view => view.Scan(prefix, ImageBound));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Records.Length).IsEqualTo(1);
        var key = KeySpace.Partition(Inbox, request.Lane.Partition, request.Lane.Queue, Item, request.HandlerScope, request.ExecutionGeneration);
        await Assert.That(page.Records.Single().Key.Span.SequenceEqual(key)).IsTrue();
        var inbox = OrleansSerialization.Deserialize<InboxRecord>(page.Records.Single().Value.Span);
        var fingerprint = JsonData.Fingerprint(new
        {
            request.Lane,
            MessageId = Item,
            request.HandlerScope,
            request.ExecutionGeneration,
            request.Effects,
            Id = Writer
        });
        await Assert.That(inbox.Fingerprint).IsEqualTo(fingerprint);
        await SharedDomainValueAssertions.EqualAsync(new
        {
            Receipt = inbox.Receipt,
            CompositionReferences = inbox.Receipt.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
        }, new
        {
            Receipt = receipt,
            CompositionReferences = receipt.Mutations.Select(mutation => mutation.CompositionReferences).ToArray()
        });
    }
}
