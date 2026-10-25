namespace KeyLoad.ClusterTests.OrleansExecution.Helpers;

/// <summary>Allows private Orleans-root deletion only after every owner and cleanup stage settled.</summary>
internal static class RequestCqrsClusterDiagnosticsRootGuard
{
    internal static bool CanDelete(bool owned, string? root, bool diagnosticsSettled,
        bool consumerSettled, bool observerJoined, bool applicationSettled, bool cleanupFailed,
        bool hasFailures)
        => owned && root is not null && diagnosticsSettled && consumerSettled && observerJoined
            && applicationSettled && !cleanupFailed && !hasFailures && Directory.Exists(root);
}
