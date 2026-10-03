using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplayTests
{
    private const string FirstEventId = "replay-event-1";
    private const string SecondEventId = "replay-event-2";
    private const string ThirdEventId = "replay-event-3";
    private const string IncrementField = "increment";
    private const string TotalField = "total";
    private const string ValueField = "value";
    private const string StreamHeadSpace = "stream-head";
    private const string OtherReducer = "other.reducer";

    [Test]
    public async Task AcEvent007CompatibleSnapshotAndTailMatchTheFullReferenceReduction()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(FirstEventId, 1), Event(SecondEventId, 2), Event(ThirdEventId, 3));
        fixture.StoreSnapshot(1, stateJson: State(1));

        var resumed = fixture.Read(maximumEvents: 3);
        var complete = fixture.Read(maximumEvents: 3, fromBeginning: true);

        await Assert.That(resumed.Snapshot!.StateJson).IsEqualTo(State(1));
        await Assert.That(resumed.Events.Select(item => item.Revision).SequenceEqual([2L, 3L])).IsTrue();
        await Assert.That(complete.Snapshot).IsNull();
        await Assert.That(complete.Events.Select(item => item.Revision).SequenceEqual([1L, 2L, 3L])).IsTrue();
        await Assert.That(Reduce(resumed.Snapshot.StateJson, resumed.Events))
            .IsEqualTo(Reduce(State(0), complete.Events));
        await Assert.That(Reduce(resumed.Snapshot.StateJson, resumed.Events)).IsEqualTo(6);
    }

    [Test]
    public async Task AcEvent007RequiresExactReducerSchemaAndCurrentGeneration()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(FirstEventId, 1));
        fixture.StoreSnapshot(1);

        var reducer = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(reducer: OtherReducer));
        var schema = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(schemaVersion: 2));
        var generation = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(generation: 2));

        await Assert.That(reducer.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(schema.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(generation.Code).IsEqualTo(ErrorCode.TokenInvalidated);
    }

    [Test]
    public async Task AcEvent007RetentionAndMissingRevisionFailWithoutReturningPartialTail()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(FirstEventId, 1), Event(SecondEventId, 2), Event(ThirdEventId, 3));
        fixture.StoreSnapshot(1);
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Delete(fixture.EventKey(2));
            return true;
        });

        var gap = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(maximumEvents: 3));
        await Assert.That(gap.Code).IsEqualTo(ErrorCode.Corruption);

        using var retained = new AggregateReplayFixture();
        retained.Append(Event(FirstEventId, 1), Event(SecondEventId, 2));
        retained.RetainFrom(2);
        var missingBeginning = Assert.ThrowsExactly<KeyLoadException>(() => retained.Read(maximumEvents: 2));
        var explicitBeginning = Assert.ThrowsExactly<KeyLoadException>(() => retained.Read(maximumEvents: 2, fromBeginning: true));
        await Assert.That(missingBeginning.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
        await Assert.That(explicitBeginning.Code).IsEqualTo(ErrorCode.HistoryUnavailable);
    }

    [Test]
    public async Task AcEvent007GenerationScopedEventIdentityMustMatchEveryTailEntry()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(FirstEventId, 1), Event(SecondEventId, 2));
        fixture.Store.Commit((transaction, _) =>
        {
            transaction.Delete(fixture.EventIdentityKey(SecondEventId));
            return true;
        });

        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(maximumEvents: 2));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcEvent007MalformedHeadAndEventSchemaFailAsCorruption()
    {
        using var badHead = new AggregateReplayFixture();
        badHead.Append(Event(FirstEventId, 1));
        badHead.Store.Commit((transaction, _) =>
        {
            var key = KeySpace.Partition(StreamHeadSpace, badHead.Partition,
                AggregateReplayFixture.StreamSet, AggregateReplayFixture.StreamId);
            transaction.PutRecord(key, new StreamHead(1, 3, 1));
            return true;
        });
        var headError = Assert.ThrowsExactly<KeyLoadException>(() => badHead.Read(maximumEvents: 1));

        using var badEvent = new AggregateReplayFixture();
        badEvent.Append(Event(FirstEventId, 1));
        badEvent.Store.Commit((transaction, _) =>
        {
            var key = badEvent.EventKey(1);
            var record = transaction.GetRecord<EventRecord>(key) ?? throw new InvalidOperationException();
            transaction.PutRecord(key, record with { Data = record.Data with { SchemaVersion = 0 } });
            return true;
        });
        var eventError = Assert.ThrowsExactly<KeyLoadException>(() => badEvent.Read(maximumEvents: 1));

        await Assert.That(headError.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(eventError.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcEvent010CompleteTailAndSharedLimitsRejectWholeRequests()
    {
        using var fixture = new AggregateReplayFixture(new() { MaxResults = 2, MaxScanRecords = 2 });
        fixture.Append(Event(FirstEventId, 1), Event(SecondEventId, 2), Event(ThirdEventId, 3));

        var tooLarge = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(maximumEvents: 3));
        var incomplete = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(maximumEvents: 2));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Read(maximumEvents: 2,
            cancellationToken: cancellation.Token));

        await Assert.That(tooLarge.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(incomplete.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcEvent010RawReadAndResultBytesAreBounded()
    {
        using var rawBudget = new AggregateReplayFixture();
        rawBudget.Append(Event(FirstEventId, 1));
        rawBudget.Reopen(new() { MaxQueryReadBytes = 32 });
        var rawError = Assert.ThrowsExactly<KeyLoadException>(() => rawBudget.Read(maximumEvents: 1));

        using var resultBudget = new AggregateReplayFixture();
        resultBudget.Append(new EventData(FirstEventId, AggregateReplayFixture.EventType,
            "{\"" + ValueField + "\":\"" + new string('x', 512) + "\"}"));
        resultBudget.Reopen(new() { MaxBatchBytes = 256 });
        var resultError = Assert.ThrowsExactly<KeyLoadException>(() => resultBudget.Read(maximumEvents: 1));

        await Assert.That(rawError.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(resultError.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcEvent010ReadDeadlineRejectsTheWholeReplay()
    {
        using var fixture = new AggregateReplayFixture(new() { QueryDeadlineSeconds = 1 },
            timeProvider: new ExpiredReplayClock());
        fixture.Append(Event(FirstEventId, 1), Event(SecondEventId, 2));
        fixture.StoreSnapshot(1);

        var deadline = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Read(maximumEvents: 2));

        await Assert.That(deadline.Code).IsEqualTo(ErrorCode.BudgetExceeded);
    }

    [Test]
    public async Task AcEvent010PureReadPreservesEventQueueAndCut()
    {
        using var fixture = new AggregateReplayFixture();
        fixture.Append(Event(FirstEventId, 1), Event(SecondEventId, 2));
        fixture.StoreSnapshot(1);
        fixture.EnqueueQueueMessage();
        var beforePosition = fixture.Position;
        var queueBefore = fixture.InspectQueueMessage();
        var eventOneBefore = fixture.Store.Read(view => view.ReadOwnedValue(fixture.EventKey(1)));
        var eventTwoBefore = fixture.Store.Read(view => view.ReadOwnedValue(fixture.EventKey(2)));

        var page = fixture.Read(maximumEvents: 2);

        var eventOneAfter = fixture.Store.Read(view => view.ReadOwnedValue(fixture.EventKey(1)));
        var eventTwoAfter = fixture.Store.Read(view => view.ReadOwnedValue(fixture.EventKey(2)));
        await Assert.That(fixture.Position).IsEqualTo(beforePosition);
        await Assert.That(page.CutPosition).IsEqualTo(beforePosition);
        await Assert.That(fixture.InspectQueueMessage()).IsEqualTo(queueBefore);
        await Assert.That(eventOneAfter!.AsSpan().SequenceEqual(eventOneBefore!)).IsTrue();
        await Assert.That(eventTwoAfter!.AsSpan().SequenceEqual(eventTwoBefore!)).IsTrue();
    }

    private static EventData Event(string eventId, int increment)
        => new(eventId, AggregateReplayFixture.EventType, "{\"" + IncrementField + "\":" + increment + "}");

    private static string State(int total) => "{ \"" + TotalField + "\" : " + total + " }";

    private static int Reduce(string stateJson, IEnumerable<EventRecord> events)
    {
        using var state = JsonDocument.Parse(stateJson);
        var total = state.RootElement.GetProperty(TotalField).GetInt32();
        foreach (var item in events)
        {
            using var payload = JsonDocument.Parse(item.Data.PayloadJson);
            total += payload.RootElement.GetProperty(IncrementField).GetInt32();
        }
        return total;
    }

    private sealed class ExpiredReplayClock : TimeProvider
    {
        private const long TimestampStep = 10 * TimeSpan.TicksPerSecond;
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override long GetTimestamp() => Interlocked.Add(ref timestamp, TimestampStep);
    }
}
