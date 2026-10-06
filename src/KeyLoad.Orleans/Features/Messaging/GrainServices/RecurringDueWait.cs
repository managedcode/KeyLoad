using System.Runtime.ExceptionServices;
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
        var primaryFailure = await AwaitFirstAndCaptureFailureAsync(changed, fallback).ConfigureAwait(true);
        var cancellationFailure = await CancelAndCaptureFailureAsync(wait).ConfigureAwait(true);
        var joinFailure = await JoinAndCaptureFailureAsync(changed, fallback, wait.IsCancellationRequested,
            primaryFailure, cancellationToken).ConfigureAwait(true);

        Rethrow(primaryFailure);
        Rethrow(cancellationFailure);
        Rethrow(joinFailure);
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

    private static void Rethrow(Exception? failure)
    {
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static async Task<Exception?> AwaitFirstAndCaptureFailureAsync(Task changed, Task fallback)
    {
        try
        {
            var winner = await Task.WhenAny(changed, fallback).ConfigureAwait(true);
            await winner.ConfigureAwait(true);
            return null;
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            return failure;
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            return failure;
        }
    }

    private static async Task<Exception?> CancelAndCaptureFailureAsync(CancellationTokenSource wait)
    {
        try
        {
            await wait.CancelAsync().ConfigureAwait(true);
            return null;
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            return failure;
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            return failure;
        }
    }

    private static async Task<Exception?> JoinAndCaptureFailureAsync(Task changed, Task fallback,
        bool waitWasCancelled, Exception? primaryFailure, CancellationToken cancellationToken)
    {
        try
        {
            await Task.WhenAll(changed, fallback).ConfigureAwait(true);
            return null;
        }
        catch (OperationCanceledException failure) when (waitWasCancelled)
        {
            return primaryFailure is null && cancellationToken.IsCancellationRequested ? failure : null;
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            return failure;
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            return failure;
        }
    }
}
