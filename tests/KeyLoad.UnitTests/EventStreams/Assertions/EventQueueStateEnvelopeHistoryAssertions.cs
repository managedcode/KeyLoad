using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeCommands;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeHistoryAssertions
{
    private const string QueueStale = "The delivery lease is stale.";
    private const string SubscriptionStale = "The subscription delivery lease is stale.";
    private const string InboxFamily = "inbox";
    private const string MissingProcessing = "The original accepted processing operation is unavailable.";

    internal static (StoreIdentity Identity, long Position, string[] Image) Capture(EventQueueStateEnvelopeFixture fixture)
    {
        return (NativeReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store), fixture.Store.Position,
            QueueWholeFlowStorage.Bytes(fixture.Store));
    }

    internal static async Task SameAsync(EventQueueStateEnvelopeFixture fixture,
        (StoreIdentity Identity, long Position, string[] Image) expected)
    {
        await NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(fixture.Store, expected.Identity, expected.Position);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Store)).IsEquivalentTo(expected.Image, CollectionOrdering.Matching);
    }

    internal static async Task ReplayAllAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeSample sample,
        CancellationToken token)
    {
        foreach (var entry in sample.History)
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
        await Assert.That(Required(fixture, KeySpace.PartitionOutcome(fixture.Partition, Root, entry.Operation.Id))
            .AsSpan().SequenceEqual(entry.OutcomeBytes)).IsTrue();
        await Assert.That(Required(fixture, KeySpace.OutcomeLocatorV2(fixture.Partition, Root, entry.Operation.Id))
            .AsSpan().SequenceEqual(entry.LocatorBytes)).IsTrue();
    }

    private static async Task ResultAsync(OperationResult actual, EventQueueStateEnvelopeHistory expected)
    {
        if (expected.ConsumedReceive)
        {
            await Assert.That(actual.Error).IsEqualTo(ErrorCode.StaleLease);
            await Assert.That(actual.SafeDetail).IsEqualTo(expected.Operation.Kind == OperationKind.Receive ? QueueStale : SubscriptionStale);
            await Assert.That(actual.Json).IsNull();
            await Assert.That(actual.NativeValue).IsNull();
            return;
        }
        if (expected.Result.NativeValue is ReceiveResult)
        {
            await NativeReplayResultAssertions.Same<ReceiveResult>(actual, expected.Result);
        }
        else if (expected.Result.NativeValue is ReceiveSubscriptionResult)
        {
            await NativeReplayResultAssertions.Same<ReceiveSubscriptionResult>(actual, expected.Result);
        }
        else if (expected.Result.NativeValue is SubscriptionInfo)
        {
            await NativeReplayResultAssertions.Same<SubscriptionInfo>(actual, expected.Result);
        }
        else
        {
            await NativeReplayResultAssertions.Same<CommitReceipt>(actual, expected.Result);
        }
    }

    internal static async Task InboxAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeSample sample)
    {
        var request = sample.Processing ?? throw new InvalidOperationException(MissingProcessing);
        var receipt = sample.ProcessingReceipt ?? throw new InvalidOperationException(MissingProcessing);
        var key = KeySpace.Partition(InboxFamily, fixture.Partition, Queue, Item, Handler, InitialGeneration);
        await Assert.That(Required(fixture, key).AsSpan().SequenceEqual(sample.InboxBytes)).IsTrue();
        var actual = NativeSerialization.Deserialize<InboxRecord>(Required(fixture, key));
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
        await NativeReplayResultAssertions.Same<CommitReceipt>(new(null)
        {
            NativeValue = actual.Receipt
        }, new(null)
        {
            NativeValue = receipt
        });
        await EventQueueStateEnvelopeSourceAssertions.EqualAsync(fixture.Database.GetDocument(Root,
            new(fixture.Partition, Documents, Processed)),
            new DocumentResult(new(fixture.Partition, Documents, Processed), InitialRevision, Payload, false, []));
    }
}
