using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class NativeEventFeedPubSubActivationFailureTests
{
    private const int SingleFailure = 1;
    private const int FirstFailure = 0;
    private const int EmptyNativeStateCount = 0;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualNativeActivationFailurePreservesWrapperAndRow(bool clear)
        => NativeEventFeedPubSubFreshStateFixture.RunAsync(fixture =>
            RunAsync(fixture, clear, TestContext.Current!.Execution.CancellationToken));

    private static async Task RunAsync(NativeEventFeedPubSubFreshStateFixture fixture,
        bool clear, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var state = new GrainState<string>(NativeEventFeedPubSubFreshStateFixture.Original);
        if (clear)
        { await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token); }
        var retained = state.State;
        var tag = state.ETag;
        var exists = state.RecordExists;
        var nativeState = fixture.CreateNativeState<List<string>>();
        await Assert.That(nativeState.Count).IsEqualTo(EmptyNativeStateCount);
        var nativeFailures = new List<Exception>();
        ServerFailureObserver.Observe(() => _ = fixture.CreateNativeState<string>(), nativeFailures);
        await Assert.That(nativeFailures.Count).IsEqualTo(SingleFailure);
        if (!NativeCqrsBoundaryErrors.IsNonFatal(nativeFailures[FirstFailure]))
        { ServerFailureObserver.ThrowIfAny(nativeFailures); }
        var actualFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => clear
            ? fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token)
            : fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token), actualFailures);
        await Assert.That(actualFailures.Count).IsEqualTo(SingleFailure);
        if (!NativeCqrsBoundaryErrors.IsNonFatal(actualFailures[FirstFailure]))
        { ServerFailureObserver.ThrowIfAny(actualFailures); }
        await Assert.That(actualFailures[FirstFailure].GetType()).IsEqualTo(nativeFailures[FirstFailure].GetType());
        await Assert.That(actualFailures[FirstFailure].Message).IsEqualTo(nativeFailures[FirstFailure].Message);
        await Assert.That(ReferenceEquals(state.State, retained)).IsTrue();
        await Assert.That(state.ETag).IsEqualTo(tag);
        await Assert.That(state.RecordExists).IsEqualTo(exists);
        if (clear)
        { await RequireStoredAsync(fixture, NativeEventFeedPubSubFreshStateFixture.Original, tag, token); }
        state.State = NativeEventFeedPubSubFreshStateFixture.Healthy;
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        await RequireStoredAsync(fixture, NativeEventFeedPubSubFreshStateFixture.Healthy, state.ETag, token);
    }

    private static async Task RequireStoredAsync(NativeEventFeedPubSubFreshStateFixture fixture,
        string value, string? tag, CancellationToken token)
    {
        var actual = new GrainState<string>();
        await fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, actual, token);
        await Assert.That(actual.State).IsEqualTo(value);
        await Assert.That(actual.ETag).IsEqualTo(tag);
        await Assert.That(actual.RecordExists).IsTrue();
    }
}
