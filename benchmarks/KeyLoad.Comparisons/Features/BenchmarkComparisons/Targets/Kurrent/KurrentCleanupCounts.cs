namespace KeyLoad.Comparisons.Targets;

internal readonly record struct KurrentCleanupCounts(int Tracked, int Submitted, int Acknowledged, int Faulted, int Pending, int PeakConcurrency)
{
    private const int NoObservedItems = 0;
    private const int MaximumEncodedCleanupConcurrency = 16;

    internal bool IsValid => Tracked >= NoObservedItems && Submitted >= NoObservedItems && Submitted <= Tracked && Acknowledged >= NoObservedItems && Faulted >= NoObservedItems && Pending >= NoObservedItems
        && (long)Acknowledged + Faulted + Pending == Submitted && Pending <= MaximumEncodedCleanupConcurrency
        && PeakConcurrency >= Pending && PeakConcurrency <= MaximumEncodedCleanupConcurrency && PeakConcurrency <= Submitted
        && (Submitted == NoObservedItems ? PeakConcurrency == NoObservedItems : PeakConcurrency > NoObservedItems);

    internal bool IsComplete => IsValid && Submitted == Tracked && Acknowledged == Tracked && Faulted == NoObservedItems && Pending == NoObservedItems;
}

internal enum KurrentCleanupStage { Delete, NativeDispose, HttpDispose, Drain, Complete }
internal enum KurrentCleanupOutcome { Succeeded, Failed }
internal enum KurrentCleanupFailureReason { None, Cancelled, NativeRpc, NativeFailure, Incomplete, DrainTimeout, Unknown }

internal sealed record KurrentCleanupDiagnostic(int SchemaVersion, KurrentCleanupStage Stage, KurrentCleanupOutcome Outcome,
    KurrentCleanupFailureReason Reason, KurrentCleanupCounts Counts, long ElapsedMilliseconds, bool CancellationRequested,
    bool DeadlineExpired, int? GrpcStatus, int LaterDisposalFailures);
