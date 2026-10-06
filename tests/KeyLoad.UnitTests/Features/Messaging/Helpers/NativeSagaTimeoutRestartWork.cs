using Orleans.DurableJobs;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class NativeSagaTimeoutRestartWork
{
    private const string TimeoutCompletionFailure = "The recovered native job did not commit its saga timeout within the bound.";
    private const int TimeoutRevision = 2;

    internal static async Task<(IJobShard Shard, NativeSagaJournalFence Fence)> WaitForReclaimAsync(
        NativeSagaTimeoutFixture fixture, DurableJob job, NativeSagaJournalFence previous,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(fixture.TestProfile.CompletionTimeout);
        while (true)
        {
            var shards = await fixture.JobHarness.RecoverShardsAsync(job.DueTime, deadline.Token);
            var matching = shards.FirstOrDefault(shard => shard.Id == job.ShardId);
            if (matching is not null)
            {
                var current = await NativeSagaTimeoutJournalFence.ReadAsync(fixture, job, deadline.Token);
                if (current.NativeOwner != previous.NativeOwner
                    && current.LocalSilo == current.NativeOwner
                    && current.CanonicalOwnerGeneration > previous.CanonicalOwnerGeneration)
                {
                    return (matching, current);
                }
            }

            await Task.Delay(fixture.TestProfile.PollInterval, deadline.Token);
        }
    }

    internal static async Task WaitForTimeoutAsync(NativeSagaTimeoutFixture fixture,
        NativeSagaTimeoutCase saga, DurableJob job, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var dueTimeRemaining = job.DueTime - TimeProvider.System.GetUtcNow();
        var completionWindow = dueTimeRemaining > TimeSpan.Zero
            ? dueTimeRemaining + fixture.TestProfile.CompletionTimeout
            : fixture.TestProfile.CompletionTimeout;
        deadline.CancelAfter(completionWindow > fixture.TestProfile.RestartCompletionTimeout
            ? fixture.TestProfile.RestartCompletionTimeout
            : completionWindow);
        try
        {
            while (true)
            {
                var current = fixture.Database.Database.InspectSaga(NativeSagaTimeoutTestData.SagaPrincipalId, saga.Lane, saga.Id, cancellationToken);
                if (current is { Phase: SagaPhase.TimedOut, Revision: TimeoutRevision })
                {
                    return;
                }

                await Task.Delay(fixture.TestProfile.PollInterval, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(TimeoutCompletionFailure);
        }
    }

    internal static Task WaitForShardsToSettleAsync(NativeSagaTimeoutFixture fixture,
        IReadOnlyCollection<IJobShard> shards, CancellationToken cancellationToken)
        => fixture.JobHarness.WaitUntilSettledAsync(shards, cancellationToken);
}
