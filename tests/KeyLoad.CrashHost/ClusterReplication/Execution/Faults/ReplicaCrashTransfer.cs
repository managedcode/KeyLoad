using KeyLoad.Replication;

namespace KeyLoad.CrashHost;

/// <summary>Real bounded source-image transfer for process interruption at snapshot publication boundaries.</summary>
internal static class ReplicaCrashTransfer
{
    private const int LastChunkByteIndex = 1;

    private const int PrefixSize = 32;

    /// <summary>The acknowledged incomplete prefix used to prove durable offset and idempotent replay.</summary>
    public static int PrefixBytes => PrefixSize;

    internal static void Run(string root, Guid incarnation, ReplicaCrashNode target, ReplicaCrashBoundary boundary)
    {
        const int SnapshotCut = 4;
        const int SnapshotTerm = 1;
        const int TargetCommittedCut = 3;
        const int UncommittedTailIndex = 5;
        const int InitialTransferOffset = 0;
        const int CorruptionBitMask = 1;

        using var source = ReplicaCrashNode.OpenSource(root, incarnation);
        source.Populate(SnapshotCut);
        var image = source.Snapshots.Create(SnapshotCut, SnapshotTerm);
        target.Populate(TargetCommittedCut);
        target.AddObsoleteRecord();
        target.Log.Append([new(SnapshotCut, SnapshotTerm, target.Database.NormalizeOperation(ReplicaCrashModel.Operation(SnapshotCut))),
            new(UncommittedTailIndex, SnapshotTerm, target.Database.NormalizeOperation(ReplicaCrashModel.Operation(UncommittedTailIndex)))]);
        var offset = target.Snapshots.Begin(image);
        var limit = boundary == ReplicaCrashBoundary.SnapshotChunkAcknowledged ? PrefixSize : target.Configuration.SnapshotChunkBytes;
        while (offset < image.Length)
        {
            var bytes = source.Snapshots.ReadChunk(image.TransferId, offset, limit);
            if (boundary == ReplicaCrashBoundary.SnapshotRejected && offset == InitialTransferOffset)
            {
                bytes[LastChunkByteIndex] ^= CorruptionBitMask;
            }
            offset = target.Snapshots.Append(image.TransferId, offset, bytes);
        }
        target.Snapshots.Complete(image.TransferId);
    }
}
