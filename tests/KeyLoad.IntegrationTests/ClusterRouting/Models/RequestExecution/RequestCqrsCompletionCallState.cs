namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal enum RequestCqrsCompletionCallState
{
    NotStarted,
    Started,
    Returned,
    Failed
}
