namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal readonly record struct RequestCqrsCaptureLifecycleSnapshot(
    TaskStatus? Node1,
    TaskStatus? Node2,
    TaskStatus? Node3,
    bool LifetimeCancellationRequested,
    bool DrainCancellationRequested,
    bool ForcedCancellationRequested,
    RequestCqrsCleanupCompletionSnapshot Completion);
