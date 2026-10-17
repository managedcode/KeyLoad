namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal readonly record struct RequestCqrsSingleTaskLifecycleSnapshot(
    TaskStatus? PendingMove,
    bool LifetimeCancellationRequested);
