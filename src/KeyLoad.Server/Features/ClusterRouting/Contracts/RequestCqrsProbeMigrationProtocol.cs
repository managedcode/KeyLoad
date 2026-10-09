namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeMigrationProtocol
{
    internal const string RequestKind = "Migration";
    internal const string RequestedKind = "MigrationRequested";
    internal const string RequestPrefix = "migration-";
    internal const string RequestedPrefix = "migration-requested-";
    internal const int SingleRequest = 1;
    internal const int FirstRequest = 0;
}
