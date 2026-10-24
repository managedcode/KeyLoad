namespace KeyLoad.IntegrationTests.OrleansExecution;

internal readonly record struct RequestCqrsSingleTaskLifecycleSnapshot(
    TaskStatus? PendingMove,
    bool LifetimeCancellationRequested);
