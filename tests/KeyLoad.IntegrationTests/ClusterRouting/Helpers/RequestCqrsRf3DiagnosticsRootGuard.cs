namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Allows private RF3-root deletion only after every owner and cleanup stage settled.</summary>
internal static class RequestCqrsRf3DiagnosticsRootGuard
{
    internal static bool CanDelete(bool owned, string? root, bool diagnosticsSettled,
        bool consumerSettled, bool observerJoined, bool applicationSettled, bool cleanupFailed,
        bool hasFailures)
        => owned && root is not null && diagnosticsSettled && consumerSettled && observerJoined
            && applicationSettled && !cleanupFailed && !hasFailures && Directory.Exists(root);
}
