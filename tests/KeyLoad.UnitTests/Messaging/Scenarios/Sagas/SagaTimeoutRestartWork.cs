using Orleans.DurableJobs;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SagaTimeoutRestartWork
{
    private const string TimeoutCompletionFailure = "The recovered execution job did not commit its saga timeout within the bound.";
    private const int TimeoutRevision = 2;

    internal static async Task<(IJobShard Shard, SagaJournalFence Fence)> WaitForReclaimAsync(
        SagaTimeoutFixture fixture, DurableJob job, SagaJournalFence previous,
        CancellationToken cancellationToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(fixture.TestProfile.CompletionTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        while (true)
        {
            var shards = await fixture.JobHarness.RecoverShardsAsync(job.DueTime, deadline.Token);
            var matching = shards.FirstOrDefault(shard => shard.Id == job.ShardId);
            if (matching is not null)
            {
                var current = await SagaTimeoutJournalFence.ReadAsync(fixture, job, deadline.Token);
                if (current.RuntimeOwner != previous.RuntimeOwner
                    && current.LocalSilo == current.RuntimeOwner
                    && current.CanonicalOwnerGeneration > previous.CanonicalOwnerGeneration)
                {
                    return (matching, current);
                }
            }

            await Task.Delay(fixture.TestProfile.PollInterval, TimeProvider.System, deadline.Token);
        }
    }

    internal static async Task WaitForTimeoutAsync(SagaTimeoutFixture fixture,
        SagaTimeoutCase saga, DurableJob job, CancellationToken cancellationToken)
    {
        var dueTimeRemaining = job.DueTime - TimeProvider.System.GetUtcNow();
        var completionWindow = dueTimeRemaining > TimeSpan.Zero
            ? dueTimeRemaining + fixture.TestProfile.CompletionTimeout
            : fixture.TestProfile.CompletionTimeout;
        using var timeout = new CancellationTokenSource(completionWindow > fixture.TestProfile.RestartCompletionTimeout
            ? fixture.TestProfile.RestartCompletionTimeout
            : completionWindow, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            while (true)
            {
                var current = fixture.Database.Database.InspectSaga(SagaTimeoutTestData.SagaPrincipalId, saga.Lane, saga.Id, cancellationToken);
                if (current is { Phase: SagaPhase.TimedOut, Revision: TimeoutRevision })
                {
                    return;
                }

                await Task.Delay(fixture.TestProfile.PollInterval, TimeProvider.System, deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(TimeoutCompletionFailure);
        }
    }

    internal static Task WaitForShardsToSettleAsync(SagaTimeoutFixture fixture,
        IReadOnlyCollection<IJobShard> shards, CancellationToken cancellationToken)
        => fixture.JobHarness.WaitUntilSettledAsync(shards, cancellationToken);
}
