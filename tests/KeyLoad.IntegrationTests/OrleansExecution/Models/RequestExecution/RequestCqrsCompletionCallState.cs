namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal enum RequestCqrsCompletionCallState
{
    NotStarted,
    Started,
    Returned,
    Failed
}
