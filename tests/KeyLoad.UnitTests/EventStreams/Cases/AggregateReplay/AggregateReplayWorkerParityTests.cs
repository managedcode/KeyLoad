using KeyLoad.Client;
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
        var reduced = new List<EventRecord>();
        var reducer = CounterReducer(apply: (state, record) =>
        {
            reduced.Add(record);
            return AddIncrement(state, record);
        });

        var allState = AggregateReplayReduction.Reduce(full, reducer, limitsOptions: UnitClientOptions.Replay());
        var resumedState = AggregateReplayReduction.Reduce(resumed, reducer, limitsOptions: UnitClientOptions.Replay());

        await Assert.That(ReadInteger(allState, "total")).IsEqualTo(9);
        await Assert.That(resumedState).IsEqualTo(allState);
        await Assert.That(ReferenceEquals(reduced[0], full.Events[0])).IsTrue();
        await Assert.That(ReferenceEquals(reduced[1], full.Events[1])).IsTrue();
        await Assert.That(ReferenceEquals(reduced[2], full.Events[2])).IsTrue();
        await Assert.That(full.Events[0].Data.PayloadJson).IsEqualTo("{\"increment\":2}");
        await Assert.That(full.Events[1].Data.PayloadJson).IsEqualTo("{\"increment\":3}");
        await Assert.That(full.Events[2].Data.PayloadJson).IsEqualTo("{\"increment\":4}");
    }
}
