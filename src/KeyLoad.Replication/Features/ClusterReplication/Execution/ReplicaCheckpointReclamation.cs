using KeyLoad.Core;

namespace KeyLoad.Replication;

internal static class ReplicaCheckpointReclamation
{
    private const int FirstLogPosition = 1;
    private const int BeforeFirstLogPosition = 0;

    internal static ReplicaSnapshot? CaptureAndReclaim(DatabaseEngine database, IDurableReplicaLog log,
        IReplicaSnapshotStore snapshots, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cut = database.LastApplied;
        var current = snapshots.Current;
        if (cut >= FirstLogPosition && cut > (current?.Index ?? BeforeFirstLogPosition))
        { current = snapshots.Create(cut, log.TermAt(cut)); }
        cancellationToken.ThrowIfCancellationRequested();
        if (current is not null)
        { _ = snapshots.ReclaimCheckpointPrefix(cancellationToken); }
        return current;
    }
}
