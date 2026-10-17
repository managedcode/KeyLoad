using KeyLoad.Clustering;
using Orleans.Storage;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class NativeEventFeedPubSubFreshStateTests
{
    [Test]
    public Task ActualMissingReadDropsRetainedNativeStateBeforeHealthyWrite()
        => NativeEventFeedPubSubFreshStateFixture.RunAsync(async fixture =>
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = NativeEventFeedPubSubFreshStateFixture.OriginalState();
        var retained = NativeEventFeedPubSubFreshStateFixture.RequireState(state);
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        var clearer = new GrainState<List<string>>();
        await fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, clearer, token);
        await fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, clearer, token);
        await fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        await fixture.RequireFreshAsync(state, retained);
        await Assert.That(retained.Single()).IsEqualTo(NativeEventFeedPubSubFreshStateFixture.Original);
        NativeEventFeedPubSubFreshStateFixture.RequireState(state).Add(NativeEventFeedPubSubFreshStateFixture.Healthy);
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        await fixture.RequireStoredAsync(NativeEventFeedPubSubFreshStateFixture.Healthy, state.ETag, token);
    });

    [Test]
    public Task ActualExistingAndMissingClearsReplaceStateBeforeHealthyWrite()
        => NativeEventFeedPubSubFreshStateFixture.RunAsync(async fixture =>
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = NativeEventFeedPubSubFreshStateFixture.OriginalState();
        var retained = NativeEventFeedPubSubFreshStateFixture.RequireState(state);
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        await fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        await fixture.RequireFreshAsync(state, retained);
        var firstReplacement = NativeEventFeedPubSubFreshStateFixture.RequireState(state);
        await fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        await fixture.RequireFreshAsync(state, firstReplacement);
        await Assert.That(retained.Single()).IsEqualTo(NativeEventFeedPubSubFreshStateFixture.Original);
        NativeEventFeedPubSubFreshStateFixture.RequireState(state).Add(NativeEventFeedPubSubFreshStateFixture.Healthy);
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        await fixture.RequireStoredAsync(NativeEventFeedPubSubFreshStateFixture.Healthy, state.ETag, token);
    });

    [Test]
    public Task ActualStaleRevisionAndWrongTypeClearLeaveCallerAndCurrentRowUnchanged()
        => NativeEventFeedPubSubFreshStateFixture.RunAsync(async fixture =>
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var stale = NativeEventFeedPubSubFreshStateFixture.OriginalState();
        var retained = stale.State;
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, stale, token);
        var originalTag = stale.ETag;
        var current = new GrainState<List<string>>([NativeEventFeedPubSubFreshStateFixture.Healthy])
        {
            ETag = originalTag,
            RecordExists = true
        };
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, current, token);
        InconsistentStateException? conflict = null;
        try
        {
            await fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, stale, token);
        }
        catch (InconsistentStateException actual) { conflict = actual; }
        await Assert.That(conflict).IsNotNull();
        await Assert.That(ReferenceEquals(stale.State, retained)).IsTrue();
        await Assert.That(stale.ETag).IsEqualTo(originalTag);
        await Assert.That(stale.RecordExists).IsTrue();
        var wrong = new GrainState<string>(NativeEventFeedPubSubFreshStateFixture.Original)
        {
            ETag = current.ETag,
            RecordExists = true
        };
        KeyLoadException? mismatch = null;
        try
        {
            await fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, wrong, token);
        }
        catch (KeyLoadException actual) { mismatch = actual; }
        await Assert.That(mismatch).IsNotNull();
        await Assert.That(mismatch!.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(mismatch.Message).IsEqualTo(EventFeedPubSubProtocol.Invalid);
        await Assert.That(wrong.State).IsEqualTo(NativeEventFeedPubSubFreshStateFixture.Original);
        await Assert.That(wrong.ETag).IsEqualTo(current.ETag);
        await Assert.That(wrong.RecordExists).IsTrue();
        await fixture.RequireStoredAsync(NativeEventFeedPubSubFreshStateFixture.Healthy, current.ETag, token);
        await fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, current, token);
        NativeEventFeedPubSubFreshStateFixture.RequireState(current).Add(NativeEventFeedPubSubFreshStateFixture.Healthy);
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, current, token);
        await fixture.RequireStoredAsync(NativeEventFeedPubSubFreshStateFixture.Healthy, current.ETag, token);
    });

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public Task ActualCancelledAndClosedCallsPreserveStateAndNativeCancellation(bool clear)
        => NativeEventFeedPubSubFreshStateFixture.RunAsync(async fixture =>
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var state = NativeEventFeedPubSubFreshStateFixture.OriginalState();
        var retained = NativeEventFeedPubSubFreshStateFixture.RequireState(state);
        await fixture.Storage.WriteStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        var tag = state.ETag;
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        OperationCanceledException? failure = null;
        try
        {
            if (clear)
            {
                await fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, cancelled.Token);
            }
            else
            {
                await fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, cancelled.Token);
            }
        }
        catch (OperationCanceledException actual) { failure = actual; }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.CancellationToken).IsEqualTo(cancelled.Token);
        await Assert.That(ReferenceEquals(state.State, retained)).IsTrue();
        await Assert.That(state.ETag).IsEqualTo(tag);
        await Assert.That(state.RecordExists).IsTrue();
        await fixture.RequireStoredAsync(NativeEventFeedPubSubFreshStateFixture.Original, tag, token);
        await fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
        var beforeClose = state.State;
        fixture.Storage.Dispose();
        ObjectDisposedException? closed = null;
        try
        {
            if (clear)
            {
                await fixture.Storage.ClearStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
            }
            else
            {
                await fixture.Storage.ReadStateAsync(EventFeedPubSubProtocol.StateName, fixture.Id, state, token);
            }
        }
        catch (ObjectDisposedException actual) { closed = actual; }
        await Assert.That(closed).IsNotNull();
        await Assert.That(ReferenceEquals(state.State, beforeClose)).IsTrue();
        await Assert.That(state.ETag).IsEqualTo(tag);
        await Assert.That(state.RecordExists).IsTrue();
    });
}
