namespace KeyLoad.IntegrationTests.OrleansExecution;

internal enum RequestCqrsCompletionCallState
{
    NotStarted,
    Started,
    Returned,
    Failed
}
