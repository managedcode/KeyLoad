namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal enum RequestCqrsLifecycleStage
{
    BuilderCreate,
    AppHostBuild,
    SubscriberAdmission,
    Scenario,
    CaptureJoin,
    IndependentConsumerJoin,
    ObserverJoin,
    ArtifactWrite,
    AppHostDispose,
    OwnedRootCleanup,
    WaveStartup,
    NodeReadiness,
    IdentityCreate,
    HeldWrite,
    PersistedRevocation,
    CaptureCompleteNode1,
    CaptureCompleteNode2,
    CaptureCompleteNode3,
    CaptureDrain,
    CaptureFallbackCancellation,
    CaptureFallbackJoin,
    CaptureLifetimeDispose,
    CaptureDeadlineDispose,
    ObserverCancellation,
    ObserverEnumeratorDispose,
    ObserverLifetimeDispose,
    IndependentComplete,
    IndependentJoin,
    IndependentEnumeratorDispose,
    IndependentLifetimeDispose,
    AuthorityDeadlineCancellation,
    AuthorityAdmissionRelease,
    AuthoritySdkJoin,
    AuthorityMcpJoin,
    AuthorityProducerDispose,
    AuthorityArmRetire,
    AuthorityCallerDispose,
    AuthorityAdministratorDispose,
    AuthorityWaveStop,
    AuthorityControlsDispose,
    AuthorityOutcomeInspect,
    AuthorityDeadlineDispose,
    AuthorityCleanupDeadlineDispose,
    AuthorityWaveAppStop,
    AuthorityWaveDiagnosticsDrain,
    AuthorityWaveAppDispose,
    AuthorityWaveLockCheck,
    AuthorityWaveDiagnosticsArtifact,
    AuthorityRootDelete
}

internal enum RequestCqrsNodeReadinessOutcome
{
    NotObserved,
    Ready,
    NotReady
}

internal readonly record struct RequestCqrsLifecycleSnapshot(
    RequestCqrsLifecycleStage Stage,
    TaskStatus? CaptureNode1,
    TaskStatus? CaptureNode2,
    TaskStatus? CaptureNode3,
    TaskStatus? AdmissionMove,
    TaskStatus? IndependentConsumerMove,
    bool CallerCancellationRequested,
    bool ParentCancellationRequested,
    bool WaveCancellationRequested,
    bool CaptureCancellationRequested,
    bool DrainCancellationRequested,
    bool CaptureFallbackRequested,
    bool ObserverCancellationRequested,
    bool ConsumerCancellationRequested,
    RequestCqrsNodeReadinessOutcome Node1Readiness,
    RequestCqrsNodeReadinessOutcome Node2Readiness,
    RequestCqrsNodeReadinessOutcome Node3Readiness,
    bool HeldWriteObserved,
    bool PersistedRevocationEntered);

internal readonly record struct RequestCqrsCaptureLifecycleSnapshot(
    TaskStatus? Node1,
    TaskStatus? Node2,
    TaskStatus? Node3,
    bool LifetimeCancellationRequested,
    bool DrainCancellationRequested,
    bool FallbackRequested);

internal readonly record struct RequestCqrsSingleTaskLifecycleSnapshot(
    TaskStatus? PendingMove,
    bool LifetimeCancellationRequested);
