using KeyLoad.Client;
using static KeyLoad.UnitTests.Features.EventStreams.AggregateReplayWorkerTestSupport;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplayWorkerValidationTests
{
    [Test]
    public async Task AcEvent010RejectsAnyWrongCurrentSchemaBeforeCallbacksThenHealthyReplaySucceeds()
    {
        var stream = Stream();
        var first = Event(stream, 1, "first", "{\"increment\":2}");
        var mismatched = Event(stream, 2, "second", "{\"increment\":3}", schema: 2);
        var invalidPage = Page(stream, null, [first, mismatched]);
        var calls = 0;
        var reducer = CounterReducer(apply: (state, record) => { calls++; return AddIncrement(state, record); });

        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(invalidPage, reducer,
            limitsOptions: UnitClientOptions.Replay()));
        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(invalidPage.Events[0].Data.PayloadJson).IsEqualTo("{\"increment\":2}");
        await Assert.That(invalidPage.Events[1].Data.SchemaVersion).IsEqualTo(2);
        var invalidPayload = Page(stream, null, [Event(stream, 1, "bad-payload", "{")]);
        var headerRecord = Event(stream, 1, "bad-headers", "{}");
        var invalidHeaders = Page(stream, null, [headerRecord with
        {
            Data = headerRecord.Data with { HeadersJson = "{" }
        }]);
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(invalidPayload, reducer,
            limitsOptions: UnitClientOptions.Replay()));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(invalidHeaders, reducer,
            limitsOptions: UnitClientOptions.Replay()));
        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(invalidPayload.Events[0].Data.PayloadJson).IsEqualTo("{");
        await Assert.That(invalidHeaders.Events[0].Data.HeadersJson).IsEqualTo("{");

        var healthyPage = Page(stream, null, [first, Event(stream, 2, "second", "{\"increment\":3}")]);
        var state = AggregateReplayReduction.Reduce(healthyPage, reducer, limitsOptions: UnitClientOptions.Replay());
        await Assert.That(ReadInteger(state, "total")).IsEqualTo(5);
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(healthyPage.Events[0].Data.PayloadJson).IsEqualTo("{\"increment\":2}");
    }

    [Test]
    public async Task AcEvent010RejectsInvalidPageVersionsIdentityAndIncompleteHeadBeforeCallbacks()
    {
        var stream = Stream();
        var record = Event(stream, 1, "one", "{}");
        var calls = 0;
        var reducer = CounterReducer(apply: (_, _) => { calls++; return "{}"; });
        var incompatibleSnapshots = new[]
        {
            Snapshot(stream, 1, "{}") with { ReducerVersion = "different.reducer" },
            Snapshot(stream, 1, "{}") with { StateSchemaVersion = 2 },
            Snapshot(stream, 1, "{")
        };
        foreach (var snapshot in incompatibleSnapshots)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(
                Page(stream, snapshot, []), reducer, limitsOptions: UnitClientOptions.Replay()));
        }
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(
            Page(stream, null, []), reducer with { InitialStateJson = "invalid" }, limitsOptions: UnitClientOptions.Replay()));
        var invalidPages = new[]
        {
            Page(stream with { Generation = 2 }, null, [record]),
            Page(stream, null, [record with { Revision = 2 }]),
            Page(stream, null, [record with { Data = record.Data with { SchemaVersion = 0 } }]),
            Page(stream, null, [record, record with { Revision = 2, EventSequence = 2 }])
        };

        foreach (var invalidPage in invalidPages)
        {
            Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(
                invalidPage, reducer, limitsOptions: UnitClientOptions.Replay()));
        }

        await Assert.That(calls).IsEqualTo(0);
    }
}
