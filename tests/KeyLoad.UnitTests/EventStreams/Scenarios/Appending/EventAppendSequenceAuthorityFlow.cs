using KeyLoad.Server;
using KeyLoad.UnitTests.Messaging;

namespace KeyLoad.UnitTests.EventStreams;

internal static class EventAppendSequenceAuthorityFlow
{
    internal const string Producer = "sequence-repaired-producer";
    internal const string Event = "sequence-repaired-event";
    internal const string HealthyProducer = "sequence-healthy-producer";
    internal const string HealthyEvent = "sequence-healthy-event";
    private const string SequenceSpace = "event-sequence";
    private const string InvalidSequence = "The persisted event sequence is invalid.";

    internal static async Task RunAsync(long sequence, CancellationToken token)
    {
        EventAppendWholeFlowFixture? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            fixture = new EventAppendWholeFlowFixture();
            await OperationAsync(fixture, sequence, token);
        }, failures);
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OperationAsync(EventAppendWholeFlowFixture fixture, long sequence, CancellationToken token)
    {
        var command = fixture.Producer(Producer, ExpectedStreamRevision.Any, EventAppendWholeFlowFixture.Data(Event));
        var key = KeySpace.Partition(SequenceSpace, fixture.Database.Partition);
        var original = fixture.Database.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original event sequence is absent.");
        await Assert.That(OrleansSerialization.Deserialize<long>(original)).IsEqualTo(1L);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture.Database.Store.Commit((tx, _) => { tx.Put(key, OrleansSerialization.Serialize(sequence)); return true; });
            var position = fixture.Database.Store.Position;
            var before = QueueWholeFlowStorage.Bytes(fixture.Database.Store);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var error = Assert.ThrowsExactly<KeyLoadException>(() => { fixture.Submit(command); });
                await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
                await Assert.That(error.Message).IsEqualTo(InvalidSequence);
                await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
                await Assert.That(QueueWholeFlowStorage.Bytes(fixture.Database.Store))
                    .IsEquivalentTo(before, TUnit.Assertions.Enums.CollectionOrdering.Matching);
                await fixture.AssertProducerAsync(Producer, false);
            }
        }, failures);
        ServerFailureObserver.Observe(() => fixture.Database.Store.Commit((tx, _) =>
        {
            tx.Put(key, original);
            return true;
        }), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(fixture.Database.Store.Read(view => view.ReadOwnedValue(key)))
            .IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        var accepted = fixture.Submit(command);
        await EventAppendSequenceAuthorityAssertions.ReceiptAsync(fixture, command, accepted, 2L);
        await fixture.AssertProducerAsync(Producer, true);
        var healthy = fixture.Producer(HealthyProducer, ExpectedStreamRevision.Exact(2), EventAppendWholeFlowFixture.Data(HealthyEvent));
        var healthyResult = fixture.Submit(healthy);
        await EventAppendSequenceAuthorityAssertions.ReceiptAsync(fixture, healthy, healthyResult, 3L);
        await fixture.AssertProducerAsync(HealthyProducer, true);
        await EventAppendSequenceAuthorityAssertions.PageAsync(fixture.Database.Database, fixture.Stream, fixture.Now, token);
        await EventAppendSequenceAuthorityAssertions.ReplayAsync(fixture.Database.Database, fixture.Database.Store,
            command, accepted, fixture.Now);
        await EventAppendSequenceAuthorityAssertions.ReplayAsync(fixture.Database.Database, fixture.Database.Store,
            healthy, healthyResult, fixture.Now);
        await EventAppendSequenceAuthorityCold.RunAsync(fixture, command, accepted, healthy, healthyResult, token);
    }
}
