using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Owns the joined, bounded wait between sequential due-discovery cycles.</summary>
internal static class RecurringDueWait
{
    /// <summary>Waits for an applied-position change or the periodic fallback, then joins both branches.</summary>
    /// <param name="consensus">The node-local canonical apply observer.</param>
    /// <param name="observedPosition">The applied cut sampled before the preceding scan.</param>
    /// <param name="fallbackInterval">The maximum wait before the next due scan.</param>
    /// <param name="clock">The service clock used by the fallback.</param>
    /// <param name="cancellationToken">Service stop cancellation.</param>
    internal static async Task WaitForChangeOrFallbackAsync(ReplicaConsensus consensus, long observedPosition,
        TimeSpan fallbackInterval, TimeProvider clock, CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var changed = consensus.WaitForAppliedPositionChangeAsync(observedPosition, wait.Token);
        var fallback = Task.Delay(fallbackInterval, clock, wait.Token);
        var winner = await Task.WhenAny(changed, fallback).ConfigureAwait(true);
        wait.Cancel();
        try
        {
            await Task.WhenAll(changed, fallback).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (wait.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (winner == changed)
        {
            _ = await changed.ConfigureAwait(true);
        }
        else
        {
            await fallback.ConfigureAwait(true);
        }
    }

    /// <summary>Enforces the minimum interval from a scan start before another page can begin.</summary>
    /// <param name="cycleStarted">Timestamp captured immediately before the prior scan.</param>
    /// <param name="minimumCadence">Minimum time between page scans.</param>
    /// <param name="clock">The service clock used to measure and wait.</param>
    /// <param name="cancellationToken">Service stop cancellation.</param>
    internal static Task WaitForMinimumCadenceAsync(long cycleStarted, TimeSpan minimumCadence,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var remaining = minimumCadence - clock.GetElapsedTime(cycleStarted, clock.GetTimestamp());
        return remaining > TimeSpan.Zero
            ? Task.Delay(remaining, clock, cancellationToken)
            : Task.CompletedTask;
    }
}
