using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.EventStreams;

internal static class StreamRecordGapAuthorityFlow
{
    private const string EventSpace = "event";

    internal static async Task RunAsync(EventAppendWholeFlowFixture fixture, long missing, CancellationToken token)
    {
        var seed = new CommandRequest(Guid.NewGuid(), fixture.Database.Partition,
            [new AppendEvents(EventAppendWholeFlowFixture.Streams, EventAppendWholeFlowFixture.StreamId,
                [EventAppendWholeFlowFixture.Data(StreamRecordKeyAuthorityFlow.Second),
                    EventAppendWholeFlowFixture.Data(StreamRecordKeyAuthorityFlow.Third)], ExpectedStreamRevision.Exact(1L))]);
        var seedResult = fixture.Submit(seed);
        await StreamRecordKeyAuthorityAssertions.ReceiptAsync(fixture, seed, seedResult, 3L);
        var forward = StreamRecordKeyAuthorityAssertions.Read(fixture.Database.Database, fixture.Stream, StreamReadDirection.Forward, 1, null, token);
        var backward = StreamRecordKeyAuthorityAssertions.Read(fixture.Database.Database, fixture.Stream, StreamReadDirection.Backward, 1, null, token);
        var key = KeySpace.Partition(EventSpace, fixture.Database.Partition,
            EventAppendWholeFlowFixture.Streams, EventAppendWholeFlowFixture.StreamId, 1L, missing);
        var original = fixture.Database.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original retained event is absent.");
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture.Database.Store.Commit((tx, _) => { tx.Delete(key); return true; });
            await StreamRecordGapAuthorityAssertions.RequireAsync(fixture, missing, forward, backward, token);
            await EventAppendSequenceAuthorityAssertions.ReplayAsync(fixture.Database.Database, fixture.Database.Store, seed, seedResult, fixture.Now);
        }, failures);
        ServerFailureObserver.Observe(() => fixture.Database.Store.Commit((tx, _) =>
        {
            tx.Put(key, original);
            return true;
        }), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(fixture.Database.Store.Read(view => view.ReadOwnedValue(key)))
            .IsEquivalentTo(original, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await StreamRecordKeyAuthorityAssertions.PagesAsync(fixture.Database.Database, fixture.Stream, fixture.Now, forward, backward, token);
        await EventAppendSequenceAuthorityAssertions.ReplayAsync(fixture.Database.Database, fixture.Database.Store, seed, seedResult, fixture.Now);
        var healthy = new CommandRequest(Guid.NewGuid(), fixture.Database.Partition,
            [new AppendEvents(EventAppendWholeFlowFixture.Streams, EventAppendWholeFlowFixture.StreamId,
                [EventAppendWholeFlowFixture.Data(StreamRecordKeyAuthorityFlow.Fourth)], ExpectedStreamRevision.Exact(3L))]);
        var healthyResult = fixture.Submit(healthy);
        await StreamRecordKeyAuthorityAssertions.ReceiptAsync(fixture, healthy, healthyResult, 4L);
        await StreamRecordKeyAuthorityAssertions.PagesFourAsync(fixture.Database.Database, fixture.Stream, fixture.Now, forward, backward, token);
        await StreamRecordKeyAuthorityCold.RunAsync(fixture, seed, seedResult, healthy, healthyResult, forward, backward, token);
    }
}
