namespace KeyLoad.ClusterTests.OrleansExecution;

internal enum RequestCqrsCompletionCallState
{
    NotStarted,
    Started,
    Returned,
    Failed
}
