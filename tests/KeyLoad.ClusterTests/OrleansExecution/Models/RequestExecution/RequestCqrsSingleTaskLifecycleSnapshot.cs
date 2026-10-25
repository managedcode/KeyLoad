namespace KeyLoad.ClusterTests.OrleansExecution;

internal readonly record struct RequestCqrsSingleTaskLifecycleSnapshot(
    TaskStatus? PendingMove,
    bool LifetimeCancellationRequested);
