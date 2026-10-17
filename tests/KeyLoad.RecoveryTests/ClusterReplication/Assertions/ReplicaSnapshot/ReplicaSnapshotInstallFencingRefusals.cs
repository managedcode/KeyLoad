using KeyLoad.CrashHost;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.RecoveryTests.Features.ClusterReplication;

internal static class ReplicaSnapshotInstallFencingRefusals
{
    internal static async Task RequireAsync(ReplicaCrashNode node, ReplicaCrashNode source, ReplicaSnapshot image)
    {
        var before = ReplicaSnapshotInstallFencingState.Capture(node);
        var foreign = Guid.NewGuid();
        await Assert.That(foreign).IsNotEqualTo(image.TransferId);
        var prefix = ReplicaCrashTransfer.PrefixBytes;
        var next = source.Snapshots.ReadChunk(image.TransferId, prefix, ReplicaSnapshotInstallFencingProtocol.OneByte);
        await RefuseAsync(node, before, () => node.Snapshots.Append(foreign, prefix, next), ErrorCode.NotFound);
        await RefuseAsync(node, before, () => node.Snapshots.Complete(foreign), ErrorCode.NotFound);
        var gap = prefix + ReplicaSnapshotInstallFencingProtocol.OneByte;
        var later = source.Snapshots.ReadChunk(image.TransferId, gap, ReplicaSnapshotInstallFencingProtocol.OneByte);
        await RefuseAsync(node, before, () => node.Snapshots.Append(image.TransferId, gap, later), ErrorCode.Conflict);
        await RefuseAsync(node, before, () => node.Snapshots.Complete(image.TransferId), ErrorCode.Conflict);
    }

    private static async Task RefuseAsync(ReplicaCrashNode node, ReplicaSnapshotInstallFencingCut before,
        Action operation, ErrorCode code)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(operation);
            await Assert.That(error.Code).IsEqualTo(code);
            await Assert.That(error.Message).IsEqualTo(ReplicaProtocol.SnapshotUnavailable);
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => ReplicaSnapshotInstallFencingState.EqualAsync(before,
            ReplicaSnapshotInstallFencingState.Capture(node)), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
