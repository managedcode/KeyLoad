namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestDrainLifecycleProtocol
{
    internal const string CallbackFailure = "The execution request drain callback failed.";
    internal const string Root = "root";
    internal const string Collection = "execution-request-drain";
    internal const string Document = "original";
    internal const string OriginalJson = "{\"value\":\"original\"}";
    internal const string ContinuedJson = "{\"value\":\"continued\"}";
    internal const string ActiveEvent = "active-stop";
    internal const string StorageEvent = "storage-stop";
    internal const string ActiveObserver = "RequestDrainLifecycle.Active";
    internal const string StorageObserver = "RequestDrainLifecycle.Storage";
}
