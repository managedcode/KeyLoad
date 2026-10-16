namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class CommandObservedReturnEdgeProtocol
{
    internal const string ClaimUnavailable = "The database request could not complete. Retry the same command ID for writes.";
    internal const string Unknown = "The write outcome is unknown. Retry the same command ID.";
}
