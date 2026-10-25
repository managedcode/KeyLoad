using KeyLoad.Server;

namespace KeyLoad.FunctionalTests.EventStreams;

internal static class StreamRecordKeyAuthorityFlow
{
    internal const string Second = "key-authority-second";
    internal const string Third = "key-authority-third";
    internal const string Fourth = "key-authority-fourth";
    private const string EventSpace = "event";

    internal static Task RunAsync(long revision, CancellationToken token)
        => RunOwnedAsync(fixture => OperationAsync(fixture, revision, token), token);

    internal static async Task RunOwnedAsync(Func<EventAppendWholeFlowFixture, Task> operation, CancellationToken token)
    {
        EventAppendWholeFlowFixture? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            token.ThrowIfCancellationRequested();
            fixture = new EventAppendWholeFlowFixture();
            await operation(fixture);
        }, failures);
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task OperationAsync(EventAppendWholeFlowFixture fixture, long revision, CancellationToken token)
    {
        var seed = new CommandRequest(Guid.NewGuid(), fixture.Database.Partition,
            [new AppendEvents(EventAppendWholeFlowFixture.Streams, EventAppendWholeFlowFixture.StreamId,
                [EventAppendWholeFlowFixture.Data(Second), EventAppendWholeFlowFixture.Data(Third)], ExpectedStreamRevision.Exact(1L))]);
        var seedResult = fixture.Submit(seed);
        await StreamRecordKeyAuthorityAssertions.ReceiptAsync(fixture, seed, seedResult, 3L);
        var forward = StreamRecordKeyAuthorityAssertions.Read(fixture.Database.Database, fixture.Stream, StreamReadDirection.Forward, 1, null, token);
        var backward = StreamRecordKeyAuthorityAssertions.Read(fixture.Database.Database, fixture.Stream, StreamReadDirection.Backward, 1, null, token);
        var key = KeySpace.Partition(EventSpace, fixture.Database.Partition,
            EventAppendWholeFlowFixture.Streams, EventAppendWholeFlowFixture.StreamId, 1L, 2L);
        var original = fixture.Database.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original second event is absent.");
        var record = OrleansSerialization.Deserialize<EventRecord>(original);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture.Database.Store.Commit((tx, _) =>
            {
                tx.Put(key, OrleansSerialization.Serialize(record with
                {
                    Revision = revision
                }));
                return true;
            });
            await StreamRecordKeyAuthorityAssertions.RefusalsAsync(fixture, forward, backward, token);
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
                [EventAppendWholeFlowFixture.Data(Fourth)], ExpectedStreamRevision.Exact(3L))]);
        var healthyResult = fixture.Submit(healthy);
        await StreamRecordKeyAuthorityAssertions.ReceiptAsync(fixture, healthy, healthyResult, 4L);
        await StreamRecordKeyAuthorityAssertions.PageAsync(fixture.Database.Database, fixture.Stream, fixture.Now,
            StreamReadDirection.Forward, null, fixture.Database.Database.LastApplied, [1L, 2L, 3L, 4L], 4L, token);
        await EventAppendSequenceAuthorityAssertions.ReplayAsync(fixture.Database.Database, fixture.Database.Store, healthy, healthyResult, fixture.Now);
        await StreamRecordKeyAuthorityCold.RunAsync(fixture, seed, seedResult, healthy, healthyResult, forward, backward, token);
    }
}
