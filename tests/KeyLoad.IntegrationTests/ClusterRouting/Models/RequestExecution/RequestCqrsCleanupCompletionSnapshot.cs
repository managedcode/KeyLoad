namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal readonly record struct RequestCqrsCleanupCompletionSnapshot(
    RequestCqrsCompletionCallSnapshot Node1,
    RequestCqrsCompletionCallSnapshot Node2,
    RequestCqrsCompletionCallSnapshot Node3,
    RequestCqrsDrainObservationSnapshot Drain);
