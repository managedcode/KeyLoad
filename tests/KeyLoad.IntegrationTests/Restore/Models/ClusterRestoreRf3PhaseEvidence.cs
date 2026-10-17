using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

[JsonConverter(typeof(JsonStringEnumConverter<ClusterRestoreRf3Phase>))]
internal enum ClusterRestoreRf3Phase
{
    Unknown, ProfileCreate, VerifiedImageRead, BuilderCreate, OriginalParameters, OriginalPeerKey,
    IsolationPrepare, BuildApplication, VerifyImageBeforeStart, ApplicationStart, SixHealthy,
    DataReadiness, VerifyStartedImages, StartupReady, SeedCreate, ScenarioEnter, ScenarioComplete,
    SeedDispose, SourceDispose
}

[JsonConverter(typeof(JsonStringEnumConverter<ClusterRestoreRf3FailureCause>))]
internal enum ClusterRestoreRf3FailureCause
{
    Unknown
}

internal sealed record ClusterRestoreRf3PhaseEvidence(ClusterRestoreRf3Phase BodyPhase,
    ClusterRestoreRf3Phase CleanupPhase, int FailureCount, bool CallerCanceled, bool DeadlineCanceled,
    TimeSpan Elapsed, ClusterRestoreRf3FailureCause Cause);
