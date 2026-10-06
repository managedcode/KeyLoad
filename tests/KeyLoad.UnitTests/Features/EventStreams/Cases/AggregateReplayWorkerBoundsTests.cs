using KeyLoad.Client;
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

        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer, limitsOptions: Microsoft.Extensions.Options.Options.Create(new KeyLoad.Client.AggregateReplayWorkerLimits() { MaximumInputBytes = 22 })));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer with { InitialStateJson = "{\"nested\":{\"deep\":true}}" }, limitsOptions: Microsoft.Extensions.Options.Options.Create(new KeyLoad.Client.AggregateReplayWorkerLimits() { MaximumJsonDepth = 1 })));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AggregateReplayReduction.Reduce(page, reducer, limitsOptions: Microsoft.Extensions.Options.Options.Create(new KeyLoad.Client.AggregateReplayWorkerLimits() { MaximumEvents = 65_537 })));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, CounterReducer(apply: (_, _) => "{\"long\":\"123456\"}") with { InitialStateJson = "0" }, limitsOptions: Microsoft.Extensions.Options.Options.Create(new KeyLoad.Client.AggregateReplayWorkerLimits() { MaximumStateBytes = 8 })));

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

        Assert.ThrowsExactly<OperationCanceledException>(() => AggregateReplayReduction.Reduce(page, CounterReducer(apply: (_, _) => { reducerCalls++; return "{}"; }), limitsOptions: UnitClientOptions.Replay(), cancellationToken: cancellation.Token));

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

        _ = AggregateReplayReduction.Reduce(page, reducer, limitsOptions: UnitClientOptions.Replay(), valid);
        await Assert.That(transformCalls).IsEqualTo(64);
        transformCalls = 0;
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, CounterReducer(66, (_, _) => { reducerCalls++; return "{}"; }), limitsOptions: UnitClientOptions.Replay(), Enumerable.Range(1, 65).Select(Upcaster)));

        using var cancellation = new CancellationTokenSource();
        transformCalls = 0;
        Assert.ThrowsExactly<OperationCanceledException>(() => AggregateReplayReduction.Reduce(page, CounterReducer(3, (_, _) => { reducerCalls++; return "{}"; }), limitsOptions: UnitClientOptions.Replay(), CancelAfterOne(cancellation), cancellationToken: cancellation.Token));
        await Assert.That(transformCalls).IsEqualTo(0);
        await Assert.That(reducerCalls).IsEqualTo(1);
    }
}
