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
    AuthorityRootDelete,
    AuthorityWaveBuilderDispose
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
    bool PersistedRevocationEntered,
    RequestCqrsScopeCompletionSnapshot ScopeCompletion,
    RequestCqrsCleanupCompletionSnapshot CleanupCompletion);

internal enum RequestCqrsCompletionCallState
{
    NotStarted,
    Started,
    Returned,
    Failed
}

internal enum RequestCqrsScopeCompletionOwner
{
    Explicit,
    Batch
}

internal readonly record struct RequestCqrsCompletionCallSnapshot(
    RequestCqrsCompletionCallState State,
    TaskStatus? StatusAtStart,
    TaskStatus? StatusAtReturn);

internal readonly record struct RequestCqrsScopeCompletionSnapshot(
    RequestCqrsCompletionCallSnapshot ExplicitNode1,
    RequestCqrsCompletionCallSnapshot ExplicitNode2,
    RequestCqrsCompletionCallSnapshot ExplicitNode3,
    RequestCqrsCompletionCallSnapshot BatchNode1,
    RequestCqrsCompletionCallSnapshot BatchNode2,
    RequestCqrsCompletionCallSnapshot BatchNode3);

internal readonly record struct RequestCqrsDrainObservationSnapshot(
    bool Started,
    bool? TokenCanceledAtStart,
    TaskStatus? StartNode1,
    TaskStatus? StartNode2,
    TaskStatus? StartNode3,
    bool? TokenCanceledAtFailure,
    TaskStatus? FailureNode1,
    TaskStatus? FailureNode2,
    TaskStatus? FailureNode3,
    bool? Returned,
    bool FallbackEntered,
    bool? CaptureCanceledAtFallback,
    bool? OriginalJoined,
    TaskStatus? JoinedNode1,
    TaskStatus? JoinedNode2,
    TaskStatus? JoinedNode3,
    bool? CaptureCanceledAfterJoin);

internal readonly record struct RequestCqrsCleanupCompletionSnapshot(
    RequestCqrsCompletionCallSnapshot Node1,
    RequestCqrsCompletionCallSnapshot Node2,
    RequestCqrsCompletionCallSnapshot Node3,
    RequestCqrsDrainObservationSnapshot Drain);

internal readonly record struct RequestCqrsCaptureLifecycleSnapshot(
    TaskStatus? Node1,
    TaskStatus? Node2,
    TaskStatus? Node3,
    bool LifetimeCancellationRequested,
    bool DrainCancellationRequested,
    bool FallbackRequested,
    RequestCqrsCleanupCompletionSnapshot Completion);

internal readonly record struct RequestCqrsSingleTaskLifecycleSnapshot(
    TaskStatus? PendingMove,
    bool LifetimeCancellationRequested);
