using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.FunctionalTests.ResourceExecution;
using KeyLoad.FunctionalTests.StorageRecovery.Assertions;
using KeyLoad.Storage;
using ManagedCode.Communication;
using TUnit.Assertions.Enums;
using static KeyLoad.FunctionalTests.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.FunctionalTests.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.FunctionalTests.EventStreams;

internal static class EventQueueStateEnvelopeHistoryAssertions
{
    private const string QueueStale = "The delivery lease is stale.";
    private const string SubscriptionStale = "The subscription delivery lease is stale.";
    private const string InboxFamily = "inbox";
    private const string MissingProcessing = "The original accepted processing operation is unavailable.";

    internal static (StoreIdentity Identity, long Position, string[] Image) Capture(EventQueueStateEnvelopeFixture fixture)
    {
        return (ReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store), fixture.Store.Position,
            QueueWholeFlowStorage.Bytes(fixture.Store));
    }

    internal static async Task SameAsync(EventQueueStateEnvelopeFixture fixture,
        (StoreIdentity Identity, long Position, string[] Image) expected)
    {
        await ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(fixture.Store, expected.Identity, expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(expected.Image, CollectionOrdering.Matching);
    }

    internal static async Task ReplayAllAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state,
        CancellationToken token)
    {
        foreach (var entry in state.History)
        {
            token.ThrowIfCancellationRequested();
            var before = Capture(fixture);
            await ResultAsync(fixture.Database.Apply(entry.Operation), entry);
            await OriginalBytesAsync(fixture, entry);
            await SameAsync(fixture, before);
            await ResultAsync(fixture.Database.ResolveOutcome(entry.Operation), entry);
            await OriginalBytesAsync(fixture, entry);
            await SameAsync(fixture, before);
        }
    }

    private static async Task OriginalBytesAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeHistory entry)
    {
        await Assert.That(Required(fixture, KeySpace.PartitionOutcome(fixture.Partition, Root, entry.Operation.CommandId))
            .AsSpan().SequenceEqual(entry.OutcomeBytes)).IsTrue();
        await Assert.That(Required(fixture, KeySpace.OutcomeLocator(fixture.Partition, Root, entry.Operation.CommandId))
            .AsSpan().SequenceEqual(entry.LocatorBytes)).IsTrue();
    }

    private static async Task ResultAsync(Result<object> actual, EventQueueStateEnvelopeHistory expected)
    {
        if (expected.ConsumedReceive)
        {
            await Assert.That(Errors.Code(actual.Problem).Value).IsEqualTo(ErrorCode.StaleLease);
            await Assert.That(actual.Problem?.Detail).IsEqualTo(expected.Operation.Value!.Kind == OperationKind.Receive ? QueueStale : SubscriptionStale);

            await Assert.That(actual.Value).IsNull();
            return;
        }
        if (expected.Result.Value is ReceiveResult)
        {
            await ReplayResultAssertions.Same<ReceiveResult>(actual, expected.Result);
        }
        else if (expected.Result.Value is ReceiveSubscriptionResult)
        {
            await ReplayResultAssertions.Same<ReceiveSubscriptionResult>(actual, expected.Result);
        }
        else if (expected.Result.Value is SubscriptionInfo)
        {
            await ReplayResultAssertions.Same<SubscriptionInfo>(actual, expected.Result);
        }
        else
        {
            await ReplayResultAssertions.Same<CommitReceipt>(actual, expected.Result);
        }
    }

    internal static async Task InboxAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state)
    {
        var request = state.Processing ?? throw new InvalidOperationException(MissingProcessing);
        var receipt = state.ProcessingReceipt ?? throw new InvalidOperationException(MissingProcessing);
        var key = KeySpace.Partition(InboxFamily, fixture.Partition, Queue, Item, Handler, InitialGeneration);
        await Assert.That(Required(fixture, key).AsSpan().SequenceEqual(state.InboxBytes)).IsTrue();
        var actual = OrleansSerialization.Deserialize<InboxRecord>(Required(fixture, key));
        var fingerprint = JsonData.Fingerprint(new
        {
            request.Lane,
            MessageId = Item,
            request.HandlerScope,
            request.ExecutionGeneration,
            request.Effects,
            Id = Root
        });
        await Assert.That(actual.Fingerprint).IsEqualTo(fingerprint);
        await ReplayResultAssertions.Same<CommitReceipt>(Result<object>.Succeed(actual.Receipt), Result<object>.Succeed(receipt));
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Database.GetDocument(Root,
            new(fixture.Partition, Documents, Processed)),
            new DocumentResult(fixture.Owner.ExpectedReference(new(fixture.Partition, Documents, Processed)), InitialRevision, Payload, false, []));
    }
}
