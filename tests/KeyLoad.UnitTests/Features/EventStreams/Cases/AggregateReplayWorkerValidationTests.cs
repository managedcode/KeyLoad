using KeyLoad.Client;
using static KeyLoad.UnitTests.Features.EventStreams.AggregateReplayWorkerTestSupport;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplayWorkerValidationTests
{
    [Test]
    public async Task AcEvent010MissingOrDuplicatePathsFailBeforeAnyCallback()
    {
        var stream = Stream();
        var page = Page(stream, null,
            [Event(stream, 1, "v1", "{}", 1), Event(stream, 2, "v2", "{}", 2)]);
        var transformCalls = 0;
        var reducerCalls = 0;
        EventUpcaster Next(int from) => new(from, from + 1,
            data => { transformCalls++; return data with { SchemaVersion = from + 1 }; });
        var backward = new EventUpcaster(2, 1, data => { transformCalls++; return data; });
        var reducer = CounterReducer(3, (_, _) => { reducerCalls++; return "{}"; });

        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer, limitsOptions:UnitClientOptions.Replay(), [Next(1)]));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer, limitsOptions:UnitClientOptions.Replay(), [Next(1), Next(1), Next(2)]));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer, limitsOptions:UnitClientOptions.Replay(), [backward]));

        await Assert.That(transformCalls).IsEqualTo(0);
        await Assert.That(reducerCalls).IsEqualTo(0);
    }

    [Test]
    public async Task AcEvent010RejectsChangedIdentityAndInvalidUpcastOutputBeforeReducer()
    {
        var stream = Stream();
        var page = Page(stream, null, [Event(stream, 1, "identity", "{}", 1)]);
        var reducerCalls = 0;
        var reducer = CounterReducer(2, (_, _) => { reducerCalls++; return "{}"; });
        var identityChange = new EventUpcaster(1, 2,
            data => data with { HeadersJson = "{\"changed\":true}", SchemaVersion = 2 });
        var invalidJson = new EventUpcaster(1, 2, data => data with { PayloadJson = "{", SchemaVersion = 2 });

        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer, limitsOptions:UnitClientOptions.Replay(), [identityChange]));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer, limitsOptions:UnitClientOptions.Replay(), [invalidJson]));

        await Assert.That(reducerCalls).IsEqualTo(0);
    }

    [Test]
    public async Task AcEvent010RejectsInvalidPageVersionsIdentityAndIncompleteHeadBeforeCallbacks()
    {
        var stream = Stream();
        var record = Event(stream, 1, "one", "{}");
        var reducerCalls = 0;
        var reducer = CounterReducer(1, (_, _) => { reducerCalls++; return "{}"; });
        var incompatibleSnapshots = new[]
        {
            Snapshot(stream, 1, "{}") with { ReducerVersion = "different.reducer" },
            Snapshot(stream, 1, "{}") with { StateSchemaVersion = 2 },
            Snapshot(stream, 1, "{")
        };
        foreach (var snapshot in incompatibleSnapshots)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(                Page(stream, snapshot, []), reducer, limitsOptions:UnitClientOptions.Replay()));
        }
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(            Page(stream, null, []), reducer with { InitialStateJson = "invalid" }, limitsOptions:UnitClientOptions.Replay()));
        var invalidPages = new[]
        {
            Page(stream with { Generation = 2 }, null, [record]),
            Page(stream, null, [record with { Revision = 2 }]),
            Page(stream, null, [record with { Data = record.Data with { SchemaVersion = 0 } }]),
            Page(stream, null, [record, record with { Revision = 2, EventSequence = 2 }])
        };

        foreach (var invalidPage in invalidPages)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(invalidPage, reducer, limitsOptions:UnitClientOptions.Replay()));
        }

        await Assert.That(reducerCalls).IsEqualTo(0);
    }
}
