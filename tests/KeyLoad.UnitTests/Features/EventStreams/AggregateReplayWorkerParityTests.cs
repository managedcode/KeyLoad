using static KeyLoad.UnitTests.Features.EventStreams.AggregateReplayWorkerTestSupport;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplayWorkerParityTests
{
    [Test]
    public async Task AcEvent007FullReplayEqualsSnapshotAndCompleteTail()
    {
        var stream = Stream();
        var full = Page(stream, null, [Event(stream, 1, "first", "{\"increment\":2}"),
            Event(stream, 2, "second", "{\"increment\":3}"), Event(stream, 3, "third", "{\"increment\":4}")]);
        var resumed = Page(stream, Snapshot(stream, 1, "{ \"total\" : 2 }"),
            [Event(stream, 2, "second", "{\"increment\":3}"), Event(stream, 3, "third", "{\"increment\":4}")]);
        var reducer = CounterReducer();

        var allState = AggregateReplayReduction.Reduce(full, reducer);
        var resumedState = AggregateReplayReduction.Reduce(resumed, reducer);

        await Assert.That(ReadInteger(allState, "total")).IsEqualTo(9);
        await Assert.That(resumedState).IsEqualTo(allState);
    }

    [Test]
    public async Task AcEvent010MultiversionUpcastingPreservesEventPositionAndMetadata()
    {
        var stream = Stream();
        var original = Event(stream, 1, "multiversion", "{\"oldValue\":7}", 1) with
        {
            RecordedAt = default,
            Data = new("multiversion", EventType, "{\"oldValue\":7}", "{\"source\":\"legacy\"}",
                1, default, "correlation", "causation")
        };
        var page = Page(stream, null, [original]);
        var upcasters = new EventUpcaster[]
        {
            new(1, 2, data => data with
            {
                PayloadJson = "{\"value\":" + ReadInteger(data.PayloadJson, "oldValue") + "}",
                SchemaVersion = 2
            }),
            new(2, 3, data => data with
            {
                PayloadJson = "{\"increment\":" + ReadInteger(data.PayloadJson, "value") + "}",
                SchemaVersion = 3
            })
        };
        EventRecord? reducedRecord = null;
        var reducer = CounterReducer(3, (_, record) =>
        {
            reducedRecord = record;
            return "{\"total\":7}";
        });

        _ = AggregateReplayReduction.Reduce(page, reducer, upcasters);

        await Assert.That(reducedRecord!.Revision).IsEqualTo(original.Revision);
        await Assert.That(reducedRecord.EventSequence).IsEqualTo(original.EventSequence);
        await Assert.That(reducedRecord.RecordedAt).IsEqualTo(original.RecordedAt);
        await Assert.That(reducedRecord.Data).IsEqualTo(original.Data with
        {
            PayloadJson = "{\"increment\":7}", SchemaVersion = 3
        });
        await Assert.That(original.Data).IsEqualTo(new EventData("multiversion", EventType,
            "{\"oldValue\":7}", "{\"source\":\"legacy\"}", 1, default, "correlation", "causation"));
    }
}
