namespace KeyLoad.IntegrationTests.OrleansExecution;

internal readonly record struct RequestCqrsCompletionCallSnapshot(
    RequestCqrsCompletionCallState State,
    TaskStatus? StatusAtStart,
    TaskStatus? StatusAtReturn);
