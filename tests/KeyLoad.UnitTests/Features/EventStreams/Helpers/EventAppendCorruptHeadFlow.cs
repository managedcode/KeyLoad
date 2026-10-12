using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventAppendCorruptHeadFlow
{
    private const string HeadSpace = "stream-head";
    private const string RejectedProducer = "corrupt-head-producer";
    private const string RejectedEvent = "corrupt-head-event";
    private const string HealthyProducer = "healthy-head-producer";
    private const string HealthyEvent = "healthy-head-event";
    private const string InvalidHead = "The persisted stream head is invalid.";

    internal static async Task RunAsync(long tail, long first, CancellationToken token)
    {
        var failures = new List<Exception>();
        EventAppendWholeFlowFixture? fixture = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                fixture = new EventAppendWholeFlowFixture();
                await InspectAsync(fixture, tail, first, token);
            }, failures);
        }
        finally
        {
            if (fixture is not null) { ServerFailureObserver.Observe(fixture.Dispose, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task InspectAsync(EventAppendWholeFlowFixture fixture, long tail, long first, CancellationToken token)
    {
        var key = KeySpace.Partition(HeadSpace, fixture.Database.Partition,
            EventAppendWholeFlowFixture.Streams, EventAppendWholeFlowFixture.StreamId);
        var original = fixture.Database.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original native stream head is absent.");
        var head = NativeSerialization.Deserialize<StreamHead>(original);
        var command = fixture.Producer(RejectedProducer, ExpectedStreamRevision.Any,
            EventAppendWholeFlowFixture.Data(RejectedEvent));
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                Replace(fixture, key, NativeSerialization.Serialize(head with { TailRevision = tail, FirstAvailableRevision = first }));
                var before = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
                var position = fixture.Database.Store.Position;
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var failure = Assert.ThrowsExactly<KeyLoadException>(() => { fixture.Submit(command); });
                    await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
                    await Assert.That(failure.Message).IsEqualTo(InvalidHead);
                    await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
                    await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store))
                        .IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                    await fixture.AssertProducerAsync(RejectedProducer, false);
                }
            }, failures);
        }
        finally
        {
            ServerFailureObserver.Observe(() => Replace(fixture, key, original), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        token.ThrowIfCancellationRequested();
        await Assert.That(fixture.Database.Store.Read(view => view.ReadOwnedValue(key)))
            .IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var accepted = fixture.Submit(command);
        accepted.Get<CommitReceipt>();
        var acceptedBytes = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
        var acceptedPosition = fixture.Database.Store.Position;
        var replay = fixture.Submit(command);
        await Assert.That(NativeSerialization.Serialize(replay))
            .IsEquivalentTo(NativeSerialization.Serialize(accepted), TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(acceptedPosition);
        await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store))
            .IsEquivalentTo(acceptedBytes, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await fixture.AssertProducerAsync(RejectedProducer, true);
        var healthy = fixture.Producer(HealthyProducer, ExpectedStreamRevision.Exact(2),
            EventAppendWholeFlowFixture.Data(HealthyEvent));
        fixture.Submit(healthy).Get<CommitReceipt>();
        await fixture.AssertProducerAsync(HealthyProducer, true);
        await fixture.AssertPageAsync(EventAppendWholeFlowFixture.Data(EventAppendWholeFlowFixture.InitialId),
            EventAppendWholeFlowFixture.Data(RejectedEvent), EventAppendWholeFlowFixture.Data(HealthyEvent));
    }

    private static void Replace(EventAppendWholeFlowFixture fixture, byte[] key, byte[] value)
        => fixture.Database.Store.Commit((transaction, _) =>
        {
            transaction.Put(key, value);
            return true;
        });
}
