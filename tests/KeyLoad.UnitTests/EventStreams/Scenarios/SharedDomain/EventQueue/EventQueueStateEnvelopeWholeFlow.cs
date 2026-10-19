using KeyLoad.Server;
using static KeyLoad.UnitTests.Features.EventStreams.EventQueueStateEnvelopeHistoryAssertions;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class EventQueueStateEnvelopeWholeFlow
{
    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new EventQueueStateEnvelopeFixture();
            await ServerFailureObserver.ObserveAsync(() => RunOwnedAsync(fixture, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunOwnedAsync(EventQueueStateEnvelopeFixture fixture, CancellationToken token)
    {
        var state = new EventQueueStateEnvelopeState();
        await EventQueueStateEnvelopeKeyAssertions.CurrentAsync(fixture);
        await EventQueueStateEnvelopeProducer.SeedAsync(fixture, state, token);
        await EventQueueStateEnvelopeQueueAssertions.CurrentAsync(fixture, state, EventQueueStateEnvelopeStage.Produced);
        await EventQueueStateEnvelopeBodyOwnershipAssertions.CallerBuffersCannotChangeProducerBodiesAsync(fixture, state);
        await EventQueueStateEnvelopeQueueContinuation.ClaimAsync(fixture, state, scheduled: false);
        await EventQueueStateEnvelopeSubscriptionContinuation.ConfigureAsync(fixture, state);
        await EventQueueStateEnvelopeSubscriptionContinuation.ClaimAsync(fixture, state);
        await CurrentAsync(fixture, state, EventQueueStateEnvelopeStage.Claimed, groupCompleted: false, token);
        var first = Capture(fixture);
        fixture.Reopen();
        await SameAsync(fixture, first);
        await CurrentAsync(fixture, state, EventQueueStateEnvelopeStage.Claimed, groupCompleted: false, token);
        await ReplayAllAsync(fixture, state, token);
        await SameAsync(fixture, first);
        await EventQueueStateEnvelopeQueueContinuation.ProcessAsync(fixture, state);
        await EventQueueStateEnvelopeSubscriptionContinuation.CompleteAsync(fixture, state);
        await CurrentAsync(fixture, state, EventQueueStateEnvelopeStage.Processed, groupCompleted: true, token);
        await EventQueueStateEnvelopeQueueContinuation.ClaimAsync(fixture, state, scheduled: true);
        await CurrentAsync(fixture, state, EventQueueStateEnvelopeStage.ScheduledClaimed, groupCompleted: true, token);
        await EventQueueStateEnvelopeQueueContinuation.CompleteScheduledAsync(fixture, state);
        await EventQueueStateEnvelopeQueueContinuation.DuplicateProcessingAsync(fixture, state);
        await CurrentAsync(fixture, state, EventQueueStateEnvelopeStage.Completed, groupCompleted: true, token);
        await ReplayAllAsync(fixture, state, token);
        var final = Capture(fixture);
        fixture.Reopen();
        await SameAsync(fixture, final);
        await CurrentAsync(fixture, state, EventQueueStateEnvelopeStage.Completed, groupCompleted: true, token);
        await ReplayAllAsync(fixture, state, token);
        await SameAsync(fixture, final);
    }

    private static async Task CurrentAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeState state,
        EventQueueStateEnvelopeStage stage, bool groupCompleted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await EventQueueStateEnvelopeKeyAssertions.CurrentAsync(fixture);
        await EventQueueStateEnvelopeSourceAssertions.CurrentAsync(fixture, state);
        await EventQueueStateEnvelopeGraphAssertions.CurrentAsync(fixture, state, token);
        await EventQueueStateEnvelopeQueueAssertions.CurrentAsync(fixture, state, stage);
        await EventQueueStateEnvelopeSubscriptionAssertions.CurrentAsync(fixture, groupCompleted);
        if (stage >= EventQueueStateEnvelopeStage.Processed)
        {
            await InboxAsync(fixture, state);
        }
    }
}
