namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeRequestDrainLifecycleProtocol
{
    internal const string CallbackFailure = "The native request drain callback failed.";
    internal const string Root = "root";
    internal const string Collection = "native-request-drain";
    internal const string Document = "original";
    internal const string OriginalJson = "{\"value\":\"original\"}";
    internal const string ContinuedJson = "{\"value\":\"continued\"}";
    internal const string ActiveEvent = "active-stop";
    internal const string StorageEvent = "storage-stop";
    internal const string ActiveObserver = "NativeRequestDrainLifecycle.Active";
    internal const string StorageObserver = "NativeRequestDrainLifecycle.Storage";
}
