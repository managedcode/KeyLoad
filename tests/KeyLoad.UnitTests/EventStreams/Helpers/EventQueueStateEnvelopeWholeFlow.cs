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
        var sample = new EventQueueStateEnvelopeSample();
        await EventQueueStateEnvelopeKeyAssertions.CurrentAsync(fixture);
        await EventQueueStateEnvelopeProducer.SeedAsync(fixture, sample, token);
        await EventQueueStateEnvelopeQueueAssertions.CurrentAsync(fixture, sample, EventQueueStateEnvelopeStage.Produced);
        await EventQueueStateEnvelopeBodyOwnershipAssertions.CallerBuffersCannotChangeNativeProducerBodiesAsync(fixture, sample);
        await EventQueueStateEnvelopeQueueContinuation.ClaimAsync(fixture, sample, scheduled: false);
        await EventQueueStateEnvelopeSubscriptionContinuation.ConfigureAsync(fixture, sample);
        await EventQueueStateEnvelopeSubscriptionContinuation.ClaimAsync(fixture, sample);
        await CurrentAsync(fixture, sample, EventQueueStateEnvelopeStage.Claimed, groupCompleted: false, token);
        var first = Capture(fixture);
        fixture.Reopen();
        await SameAsync(fixture, first);
        await CurrentAsync(fixture, sample, EventQueueStateEnvelopeStage.Claimed, groupCompleted: false, token);
        await ReplayAllAsync(fixture, sample, token);
        await SameAsync(fixture, first);
        await EventQueueStateEnvelopeQueueContinuation.ProcessAsync(fixture, sample);
        await EventQueueStateEnvelopeSubscriptionContinuation.CompleteAsync(fixture, sample);
        await CurrentAsync(fixture, sample, EventQueueStateEnvelopeStage.Processed, groupCompleted: true, token);
        await EventQueueStateEnvelopeQueueContinuation.ClaimAsync(fixture, sample, scheduled: true);
        await CurrentAsync(fixture, sample, EventQueueStateEnvelopeStage.ScheduledClaimed, groupCompleted: true, token);
        await EventQueueStateEnvelopeQueueContinuation.CompleteScheduledAsync(fixture, sample);
        await EventQueueStateEnvelopeQueueContinuation.DuplicateProcessingAsync(fixture, sample);
        await CurrentAsync(fixture, sample, EventQueueStateEnvelopeStage.Completed, groupCompleted: true, token);
        await ReplayAllAsync(fixture, sample, token);
        var final = Capture(fixture);
        fixture.Reopen();
        await SameAsync(fixture, final);
        await CurrentAsync(fixture, sample, EventQueueStateEnvelopeStage.Completed, groupCompleted: true, token);
        await ReplayAllAsync(fixture, sample, token);
        await SameAsync(fixture, final);
    }

    private static async Task CurrentAsync(EventQueueStateEnvelopeFixture fixture, EventQueueStateEnvelopeSample sample,
        EventQueueStateEnvelopeStage stage, bool groupCompleted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await EventQueueStateEnvelopeKeyAssertions.CurrentAsync(fixture);
        await EventQueueStateEnvelopeSourceAssertions.CurrentAsync(fixture, sample);
        await EventQueueStateEnvelopeGraphAssertions.CurrentAsync(fixture, sample, token);
        await EventQueueStateEnvelopeQueueAssertions.CurrentAsync(fixture, sample, stage);
        await EventQueueStateEnvelopeSubscriptionAssertions.CurrentAsync(fixture, groupCompleted);
        if (stage >= EventQueueStateEnvelopeStage.Processed)
        {
            await InboxAsync(fixture, sample);
        }
    }
}
