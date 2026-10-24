using KeyLoad.Clustering;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.EventStreams;

internal sealed class EventFeedPubSubActivationFailureTests
{
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;
    private const int EmptyStateCount = 0;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualActivationFailurePreservesWrapperAndRow(bool clear)
        => EventFeedPubSubFreshStateFixture.RunAsync(fixture =>
            RunAsync(fixture, clear, TestContext.Current!.Execution.CancellationToken));

    private static async Task RunAsync(EventFeedPubSubFreshStateFixture fixture,
        bool clear, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = new GrainState<string>(EventFeedPubSubFreshStateFixture.Original);
        if (clear)
        {
            await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        }
        var retained = state.State;
        var tag = state.ETag;
        var exists = state.RecordExists;
        var runtimeState = fixture.RuntimeCreateState<List<string>>();
        await Assert.That(runtimeState.Count).IsEqualTo(EmptyStateCount);
        var runtimeFailures = new List<Exception>();
        ServerFailureObserver.Observe(() => _ = fixture.RuntimeCreateState<string>(), runtimeFailures);
        await Assert.That(runtimeFailures.Count).IsEqualTo(SingleFailure);
        if (!CqrsBoundaryErrors.IsNonFatal(runtimeFailures[FirstFailure]))
        {
            ServerFailureObserver.ThrowIfAny(runtimeFailures);
        }
        var actualFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => clear
            ? fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token)
            : fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token), actualFailures);
        await Assert.That(actualFailures.Count).IsEqualTo(SingleFailure);
        if (!CqrsBoundaryErrors.IsNonFatal(actualFailures[FirstFailure]))
        {
            ServerFailureObserver.ThrowIfAny(actualFailures);
        }
        await Assert.That(actualFailures[FirstFailure].GetType()).IsEqualTo(runtimeFailures[FirstFailure].GetType());
        await Assert.That(actualFailures[FirstFailure].Message).IsEqualTo(runtimeFailures[FirstFailure].Message);
        await Assert.That(ReferenceEquals(state.State, retained)).IsTrue();
        await Assert.That(state.ETag).IsEqualTo(tag);
        await Assert.That(state.RecordExists).IsEqualTo(exists);
        if (clear)
        {
            await RequireStoredAsync(fixture, EventFeedPubSubFreshStateFixture.Original, tag, token);
        }
        state.State = EventFeedPubSubFreshStateFixture.Healthy;
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        await RequireStoredAsync(fixture, EventFeedPubSubFreshStateFixture.Healthy, state.ETag, token);
    }

    private static async Task RequireStoredAsync(EventFeedPubSubFreshStateFixture fixture,
        string value, string? tag, CancellationToken token)
    {
        var actual = new GrainState<string>();
        await fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, actual, token);
        await Assert.That(actual.State).IsEqualTo(value);
        await Assert.That(actual.ETag).IsEqualTo(tag);
        await Assert.That(actual.RecordExists).IsTrue();
    }
}
