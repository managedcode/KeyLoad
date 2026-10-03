using KeyLoad.Replication;

namespace KeyLoad.CrashHost;

/// <summary>Real bounded source-image transfer for process interruption at snapshot publication boundaries.</summary>
internal static class ReplicaCrashTransfer
{
    private const int PrefixSize = 32;

    /// <summary>The acknowledged incomplete prefix used to prove durable offset and idempotent replay.</summary>
    public static int PrefixBytes => PrefixSize;

    internal static void Run(string root, Guid incarnation, ReplicaCrashNode target, ReplicaCrashBoundary boundary)
    {
        using var source = ReplicaCrashNode.OpenSource(root, incarnation);
        source.Populate(4);
        var image = source.Snapshots.Create(4, 1);
        target.Populate(3);
        target.AddObsoleteRecord();
        target.Log.Append([new(4, 1, target.Database.NormalizeOperation(ReplicaCrashModel.Operation(4))),
            new(5, 1, target.Database.NormalizeOperation(ReplicaCrashModel.Operation(5)))]);
        var offset = target.Snapshots.Begin(image);
        var limit = boundary == ReplicaCrashBoundary.SnapshotChunkAcknowledged ? PrefixSize : target.Configuration.SnapshotChunkBytes;
        while (offset < image.Length)
        {
            var bytes = source.Snapshots.ReadChunk(image.TransferId, offset, limit);
            if (boundary == ReplicaCrashBoundary.SnapshotRejected && offset == 0)
            {
                bytes[^1] ^= 1;
            }
            offset = target.Snapshots.Append(image.TransferId, offset, bytes);
        }
        target.Snapshots.Complete(image.TransferId);
    }
}
