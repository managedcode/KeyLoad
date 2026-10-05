using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KurrentCleanupState(int tracked)
{
    private readonly System.Threading.Lock gate = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private int next, submitted, acknowledged, faulted, pending, peak, laterDisposals;
    private ExceptionDispatchInfo? primary;
    private ExceptionDispatchInfo? firstFatal;
    private KurrentCleanupStage failedStage;
    private KurrentCleanupFailureReason reason;
    private int? grpcStatus;
    private bool cancelled, deadlineExpired;
    private Task? cancellationCallbacks;

    internal TimeSpan Remaining => TimeSpan.FromSeconds(KurrentConstants.CleanupHostTimeoutSeconds) - clock.Elapsed;

    internal Task CancellationCallbacks
    {
        get
        {
            lock (gate)
            {
                return cancellationCallbacks ?? Task.CompletedTask;
            }
        }
    }

    internal void Cancel(CancellationTokenSource source)
    {
        lock (gate)
        {
            if (cancellationCallbacks is not null)
            {
                return;
            }
            try
            {
                cancellationCallbacks = source.CancelAsync();
            }
            catch (ObjectDisposedException error)
            {
                Capture(error, KurrentCleanupStage.Drain);
                cancellationCallbacks = Task.CompletedTask;
            }
        }
    }

    internal bool TrySubmit(CancellationToken token, out int index, CancellationToken deadline = default)
    {
        lock (gate)
        {
            index = next;
            if (primary is not null || token.IsCancellationRequested || deadline.IsCancellationRequested || next == tracked)
            {
                return false;
            }
            next++;
            submitted++;
            pending++;
            peak = Math.Max(peak, pending);
            return true;
        }
    }

    internal void CompleteDelete(Exception? error)
    {
        lock (gate)
        {
            pending--;
            if (error is null)
            {
                acknowledged++;
            }
            else
            {
                faulted++;
                Capture(error, KurrentCleanupStage.Delete);
            }
        }
    }

    internal void RecordFailure(Exception error, KurrentCleanupStage stage, bool disposal = false)
    {
        lock (gate)
        {
            if (disposal && primary is not null)
            {
                laterDisposals++;
            }
            Capture(error, stage);
        }
    }

    internal void FinishDeletion(bool cancellationRequested, bool expired)
    {
        lock (gate)
        {
            cancelled = cancellationRequested;
            deadlineExpired = expired;
            if (primary is null && (expired || !Counts().IsComplete))
            {
                Capture(new ComparisonFailureException(KurrentConstants.CleanupIncomplete), KurrentCleanupStage.Delete);
                reason = KurrentCleanupFailureReason.Incomplete;
            }
        }
    }

    internal KurrentCleanupDiagnostic Snapshot()
    {
        lock (gate)
        {
            return new(1, primary is null ? KurrentCleanupStage.Complete : failedStage,
                primary is null ? KurrentCleanupOutcome.Succeeded : KurrentCleanupOutcome.Failed,
                reason, Counts(), clock.ElapsedMilliseconds, cancelled, deadlineExpired, grpcStatus, laterDisposals);
        }
    }

    internal void ThrowIfFailed()
    {
        ExceptionDispatchInfo? failure;
        lock (gate)
        {
            failure = firstFatal ?? primary;
        }
        failure?.Throw();
    }

    private KurrentCleanupCounts Counts() => new(tracked, submitted, acknowledged, faulted, pending, peak);

    private void Capture(Exception error, KurrentCleanupStage stage)
    {
        if (firstFatal is null)
        {
            firstFatal = KurrentCleanupFatalCause.Find(error);
        }
        var classified = KurrentCleanupDiagnostics.Classify(error);
        if (primary is not null)
        {
            if (failedStage == stage)
            {
                grpcStatus ??= classified.GrpcStatus;
            }
            return;
        }
        primary = ExceptionDispatchInfo.Capture(error);
        failedStage = stage;
        (reason, grpcStatus) = classified;
        if (stage == KurrentCleanupStage.Drain && error is TimeoutException)
        {
            reason = KurrentCleanupFailureReason.DrainTimeout;
        }
    }
}
