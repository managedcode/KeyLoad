namespace KeyLoad.Comparisons.Targets;

internal readonly record struct KurrentCleanupCounts(int Tracked, int Submitted, int Acknowledged, int Faulted, int Pending, int PeakConcurrency)
{
    internal bool IsValid => Tracked >= 0 && Submitted >= 0 && Submitted <= Tracked && Acknowledged >= 0 && Faulted >= 0 && Pending >= 0
        && (long)Acknowledged + Faulted + Pending == Submitted && Pending <= KurrentConstants.CleanupConcurrency
        && PeakConcurrency >= Pending && PeakConcurrency <= KurrentConstants.CleanupConcurrency && PeakConcurrency <= Submitted
        && (Submitted == 0 ? PeakConcurrency == 0 : PeakConcurrency > 0);

    internal bool IsComplete => IsValid && Submitted == Tracked && Acknowledged == Tracked && Faulted == 0 && Pending == 0;
}

internal enum KurrentCleanupStage { Delete, NativeDispose, HttpDispose, Drain, Complete }
internal enum KurrentCleanupOutcome { Succeeded, Failed }
internal enum KurrentCleanupFailureReason { None, Cancelled, NativeRpc, NativeFailure, Incomplete, DrainTimeout, Unknown }

internal sealed record KurrentCleanupDiagnostic(int SchemaVersion, KurrentCleanupStage Stage, KurrentCleanupOutcome Outcome,
    KurrentCleanupFailureReason Reason, KurrentCleanupCounts Counts, long ElapsedMilliseconds, bool CancellationRequested,
    bool DeadlineExpired, int? GrpcStatus, int LaterDisposalFailures);
