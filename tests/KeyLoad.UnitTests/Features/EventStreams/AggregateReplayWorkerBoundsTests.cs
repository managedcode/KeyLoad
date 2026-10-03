using static KeyLoad.UnitTests.Features.EventStreams.AggregateReplayWorkerTestSupport;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplayWorkerBoundsTests
{
    [Test]
    public async Task AcEvent010EnforcesInputStateDepthAndHardLimits()
    {
        var stream = Stream();
        var page = Page(stream, null, [Event(stream, 1, "large", "{\"value\":\"123456\"}")]);
        var reducer = CounterReducer();

        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer,
            limits: new() { MaximumInputBytes = 22 }));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page,
            reducer with { InitialStateJson = "{\"nested\":{\"deep\":true}}" },
            limits: new() { MaximumJsonDepth = 1 }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AggregateReplayReduction.Reduce(page, reducer,
            limits: new() { MaximumEvents = 65_537 }));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page,
            CounterReducer(apply: (_, _) => "{\"long\":\"123456\"}") with { InitialStateJson = "0" },
            limits: new() { MaximumStateBytes = 8 }));

        await Assert.That(page.Events[0].Data.PayloadJson).IsEqualTo("{\"value\":\"123456\"}");
    }

    [Test]
    public async Task AcEvent010CancellationBeforeReductionDoesNotInvokeCallbacks()
    {
        var stream = Stream();
        var page = Page(stream, null, [Event(stream, 1, "cancel", "{}")]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var reducerCalls = 0;

        Assert.ThrowsExactly<OperationCanceledException>(() => AggregateReplayReduction.Reduce(page,
            CounterReducer(apply: (_, _) => { reducerCalls++; return "{}"; }), cancellationToken: cancellation.Token));

        await Assert.That(reducerCalls).IsEqualTo(0);
    }

    [Test]
    public async Task AcEvent010BoundsRegistryAndCancelsWhileEnumeratingBeforeCallbacks()
    {
        var stream = Stream();
        var page = Page(stream, null, [Event(stream, 1, "bounded", "{}")]);
        var transformCalls = 0;
        var reducerCalls = 0;
        EventUpcaster Upcaster(int from) => new(from, from + 1, data =>
        {
            transformCalls++;
            return data with { SchemaVersion = from + 1 };
        });
        var reducer = CounterReducer(65, (_, _) => { reducerCalls++; return "{}"; });
        var valid = Enumerable.Range(1, 64).Select(Upcaster).ToArray();

        _ = AggregateReplayReduction.Reduce(page, reducer, valid);
        await Assert.That(transformCalls).IsEqualTo(64);
        transformCalls = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(
            page, CounterReducer(66, (_, _) => { reducerCalls++; return "{}"; }),
            Enumerable.Range(1, 65).Select(Upcaster)));

        using var cancellation = new CancellationTokenSource();
        transformCalls = 0;
        Assert.ThrowsExactly<OperationCanceledException>(() => AggregateReplayReduction.Reduce(
            page, CounterReducer(3, (_, _) => { reducerCalls++; return "{}"; }),
            CancelAfterOne(cancellation), cancellationToken: cancellation.Token));
        await Assert.That(transformCalls).IsEqualTo(0);
        await Assert.That(reducerCalls).IsEqualTo(1);
    }
}
