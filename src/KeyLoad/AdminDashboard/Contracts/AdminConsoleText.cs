namespace KeyLoad.Server;

internal static class AdminConsoleText
{
    internal const string DefaultScope = "root";
    internal const string DefaultPartition = "default";
    internal const string Connect = "Connect to explore live database observations.";
    internal const string Disconnected = "Disconnected. Connect to explore live database observations.";
    internal const string Updated = "Live observations updated.";
    internal const string LoadingResources = "Loading resources…";
    internal const string ResourcesReady = "Resources updated.";
    internal const string LoadingRecords = "Loading records…";
    internal const string RecordsReady = "Records updated.";
    internal const string Unauthorized = "Administrator authorization failed.";
    internal const string Unavailable = "Observation unavailable. Reconnect to retry.";
    internal const string ReadFailed = "The authorized console read failed.";
    internal const string NoAddress = "The server has no listening address.";
    internal const string NoResource = "Select a resource.";
    internal const string Records = "Metadata and records are read without changing stored data.";
    internal const string Files = "Published file metadata; payload bytes remain in physical storage.";
    internal const string Unsupported = "This resource kind does not support record browsing.";
    internal const string QueueCounts = "{0} stored · {1} in flight · Non-consuming metadata";
    internal const string Bearer = "Bearer";
    internal const string AnyIpv6 = "[::]";
    internal const string BareIpv6 = "::";
    internal const string AnyIpv4 = "0.0.0.0";
    internal const string WildcardHost = "*";
    internal const string AllHosts = "+";
    internal const string QueueView = "queues";
    internal const string FilesView = "files";
    internal const string Wildcard = "*";
    internal const string StorageRoot = "/api/storage/";
    internal const string StorageList = "/list";
    internal const string AfterQuery = "?after=";
    internal const char PathSeparator = '/';
}
