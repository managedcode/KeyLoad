using KeyLoad.Storage;

namespace KeyLoad.Replication;

internal static class ReplicaVerifiedPrefixReclamation
{
    private const int NoReclaimedEntries = 0;

    internal static int Reclaim(IAtomicStore canonical, IDurableReplicaLog log, ReplicaConfiguration configuration,
        ReplicaSnapshotFiles files, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var published = log.State.Snapshot;
        if (published is null)
        { return NoReclaimedEntries; }
        ReplicaPersistence.ValidateSnapshot(published, configuration);
        ReplicaPersistence.VerifyImage(canonical, files.ImagePath(published), published);
        return log.ReclaimCheckpointPrefix(published, cancellationToken);
    }
}
