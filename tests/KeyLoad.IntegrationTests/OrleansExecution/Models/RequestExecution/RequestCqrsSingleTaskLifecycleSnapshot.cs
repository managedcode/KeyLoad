namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal readonly record struct RequestCqrsSingleTaskLifecycleSnapshot(
    TaskStatus? PendingMove,
    bool LifetimeCancellationRequested);
