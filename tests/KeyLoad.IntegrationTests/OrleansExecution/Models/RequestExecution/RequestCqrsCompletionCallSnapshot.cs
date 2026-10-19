namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal readonly record struct RequestCqrsCompletionCallSnapshot(
    RequestCqrsCompletionCallState State,
    TaskStatus? StatusAtStart,
    TaskStatus? StatusAtReturn);
