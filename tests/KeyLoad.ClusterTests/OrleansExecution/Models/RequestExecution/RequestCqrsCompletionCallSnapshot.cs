namespace KeyLoad.ClusterTests.OrleansExecution;

internal readonly record struct RequestCqrsCompletionCallSnapshot(
    RequestCqrsCompletionCallState State,
    TaskStatus? StatusAtStart,
    TaskStatus? StatusAtReturn);
