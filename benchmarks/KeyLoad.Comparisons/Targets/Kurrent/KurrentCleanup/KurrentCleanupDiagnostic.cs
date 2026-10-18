namespace KeyLoad.Comparisons.Targets;

internal sealed record KurrentCleanupDiagnostic(int SchemaVersion, KurrentCleanupStage Stage, KurrentCleanupOutcome Outcome,
    KurrentCleanupFailureReason Reason, KurrentCleanupCounts Counts, long ElapsedMilliseconds, bool CancellationRequested,
    bool DeadlineExpired, int? GrpcStatus, int LaterDisposalFailures);
