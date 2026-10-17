namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed record ClusterRestoreRf3PhaseEvidence(ClusterRestoreRf3Phase BodyPhase,
    ClusterRestoreRf3Phase CleanupPhase, int FailureCount, bool CallerCanceled, bool DeadlineCanceled,
    TimeSpan Elapsed, ClusterRestoreRf3FailureCause Cause);
