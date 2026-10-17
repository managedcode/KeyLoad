namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal readonly record struct RequestCqrsCompletionCallSnapshot(
    RequestCqrsCompletionCallState State,
    TaskStatus? StatusAtStart,
    TaskStatus? StatusAtReturn);
