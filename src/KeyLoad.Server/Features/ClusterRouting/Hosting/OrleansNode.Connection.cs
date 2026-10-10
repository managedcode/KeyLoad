using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

internal sealed partial class OrleansNode
{
    internal Task CloseConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
        => OrleansNodeRequestExecutor.CloseAsync(Grains ?? throw Errors.Fail(ErrorCode.OwnershipLost, OrleansNodeProtocol.RoutingUnavailable),
            RuntimeServices, connectionId, cancellationToken);
    internal RequestCqrsReplicaSnapshotObserver? ReplicaSnapshotObserver
        => Grains is null ? null : RequestCqrsReplicaSnapshotObserver.Find(Volatile.Read(ref host)?.Services);
    internal Task WaitForNativeStartupAsync(CancellationToken cancellationToken)
        => OrleansNodeStartupWait.WaitAsync(lifecycle, ref startup, cancellationToken);
}
