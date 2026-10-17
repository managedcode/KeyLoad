namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal readonly record struct RequestCqrsCaptureLifecycleSnapshot(
    TaskStatus? Node1,
    TaskStatus? Node2,
    TaskStatus? Node3,
    bool LifetimeCancellationRequested,
    bool DrainCancellationRequested,
    bool FallbackRequested,
    RequestCqrsCleanupCompletionSnapshot Completion);
