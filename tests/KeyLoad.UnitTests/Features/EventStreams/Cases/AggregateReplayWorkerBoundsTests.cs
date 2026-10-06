using KeyLoad.Client;
using static KeyLoad.UnitTests.Features.EventStreams.AggregateReplayWorkerTestSupport;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class AggregateReplayWorkerBoundsTests
{
    [Test]
    public async Task AcEvent010EnforcesInputStateDepthEventAndHardLimitsBeforeReduction()
    {
        var stream = Stream();
        var page = Page(stream, null, [Event(stream, 1, "large", "{\"value\":\"123456\"}")]);
        var calls = 0;
        var reducer = CounterReducer(apply: (_, _) => { calls++; return "{\"total\":0}"; });

        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page, reducer,
            Microsoft.Extensions.Options.Options.Create(new AggregateReplayWorkerLimits { MaximumInputBytes = 22 })));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page,
            reducer with { InitialStateJson = "{\"nested\":{\"deep\":true}}" },
            Microsoft.Extensions.Options.Options.Create(new AggregateReplayWorkerLimits { MaximumJsonDepth = 1 })));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AggregateReplayReduction.Reduce(page, reducer,
            Microsoft.Extensions.Options.Options.Create(new AggregateReplayWorkerLimits { MaximumEvents = 65_537 })));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(
            Page(stream, null, [Event(stream, 1, "one", "{}"), Event(stream, 2, "two", "{}")]), reducer,
            Microsoft.Extensions.Options.Options.Create(new AggregateReplayWorkerLimits { MaximumEvents = 1 })));
        Assert.ThrowsExactly<InvalidDataException>(() => AggregateReplayReduction.Reduce(page,
            CounterReducer(apply: (_, _) => "{\"long\":\"123456\"}") with { InitialStateJson = "0" },
            Microsoft.Extensions.Options.Options.Create(new AggregateReplayWorkerLimits { MaximumStateBytes = 8 })));

        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(page.Events[0].Data.PayloadJson).IsEqualTo("{\"value\":\"123456\"}");
    }

    [Test]
    public async Task AcEvent010CancellationHasNoReducerEffectsAndHealthyReplayStillWorks()
    {
        var stream = Stream();
        var page = Page(stream, null, [Event(stream, 1, "cancel", "{\"increment\":2}")]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var calls = 0;
        var reducer = CounterReducer(apply: (state, record) => { calls++; return AddIncrement(state, record); });

        Assert.ThrowsExactly<OperationCanceledException>(() => AggregateReplayReduction.Reduce(page, reducer,
            limitsOptions: UnitClientOptions.Replay(), cancellationToken: cancellation.Token));
        await Assert.That(calls).IsEqualTo(0);
        await Assert.That(page.Events[0].Data.PayloadJson).IsEqualTo("{\"increment\":2}");

        var state = AggregateReplayReduction.Reduce(page, reducer, limitsOptions: UnitClientOptions.Replay());
        await Assert.That(ReadInteger(state, "total")).IsEqualTo(2);
        await Assert.That(calls).IsEqualTo(1);
    }
}
