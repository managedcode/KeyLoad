using KeyLoad.Clustering;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.GraphTraversal;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Publishes a terminal physical-close permit only after every original borrower cleanup succeeds.</summary>
internal sealed class OrleansNodeClosure
{
    private const int NoCleanupFailures = 0;
    private bool completed;

    internal bool IsJoined => Volatile.Read(ref completed);

    internal static bool KnownOwnersJoined(NativeRequestWorkOwner work, RemoteDocumentRuntime? documents,
        PartitionMovementRuntime? movement, RemoteGraphNodeResources graphs)
        => work.IsJoined && (documents?.IsJoined ?? true) && (movement?.IsJoined ?? true) && graphs.IsJoined;

    internal void Complete(IReadOnlyCollection<Exception> cleanupFailures)
    {
        if (cleanupFailures.Count == NoCleanupFailures)
        {
            Volatile.Write(ref completed, true);
        }
    }
}
