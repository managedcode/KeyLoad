namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

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
    bool CaptureForcedCancellationRequested,
    bool ObserverCancellationRequested,
    bool ConsumerCancellationRequested,
    RequestCqrsNodeReadinessOutcome Node1Readiness,
    RequestCqrsNodeReadinessOutcome Node2Readiness,
    RequestCqrsNodeReadinessOutcome Node3Readiness,
    bool HeldWriteObserved,
    bool PersistedRevocationEntered,
    RequestCqrsScopeCompletionSnapshot ScopeCompletion,
    RequestCqrsCleanupCompletionSnapshot CleanupCompletion);
