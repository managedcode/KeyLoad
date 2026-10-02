using System.Security.Cryptography;
using ZoneTree;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointWriter
{
    internal static StorageSnapshot Write(
        string path,
        ZoneTreeStoreOptions options,
        StoreIdentity identity,
        long position,
        long appliedPosition,
        IZoneTree<Memory<byte>, Memory<byte>> tree)
    {
        var metadata = new ZoneTreeCheckpointMetadata(
            ZoneTreePersistenceFormat.CheckpointVersion,
            KeyCodec.Version,
            identity.Incarnation,
            position,
            appliedPosition);
        using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            ZoneTreePersistenceFormat.FileBufferBytes,
            FileOptions.WriteThrough);
        SetPrivateMode(path);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointMagic, metadata.Position,
            JsonDefaults.Serialize(metadata), digest);
        var records = WriteRecords(output, options, metadata.Position, tree, digest);
        var footer = new ZoneTreeCheckpointFooter(records, Convert.ToHexStringLower(digest.GetHashAndReset()));
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointEndMagic, metadata.Position,
            JsonDefaults.Serialize(footer), null);
        options.FaultObserver?.Invoke(CommitStage.SnapshotWritten, metadata.Position, 0);
        output.Flush(true);
        options.FaultObserver?.Invoke(CommitStage.SnapshotFlushed, metadata.Position, 0);
        return new(metadata.Incarnation, metadata.Position, metadata.AppliedPosition, records);
    }

    private static long WriteRecords(
        FileStream output,
        ZoneTreeStoreOptions options,
        long position,
        IZoneTree<Memory<byte>, Memory<byte>> tree,
        IncrementalHash digest)
    {
        var batch = new List<StorageMutation>();
        long batchBytes = 0;
        long records = 0;
        using (var iterator = tree.CreateIterator(IteratorType.NoRefresh))
        {
            while (iterator.Next())
            {
                var item = new StorageMutation(iterator.CurrentKey.ToArray(), iterator.CurrentValue.Span[
                    ZoneTreePersistenceFormat.StorageValueHeaderBytes..].ToArray());
                batch.Add(item);
                batchBytes += item.Key.Length + item.Value!.Value.Length;
                records++;
                if (batchBytes >= ZoneTreePersistenceFormat.CheckpointBatchBytes
                    || batch.Count >= ZoneTreePersistenceFormat.CheckpointBatchRecords)
                {
                    WriteBatch(output, options, position, batch, digest);
                    batch.Clear();
                    batchBytes = 0;
                }
            }
        }

        if (batch.Count != 0)
        {
            WriteBatch(output, options, position, batch, digest);
        }

        return records;
    }

    private static void WriteBatch(
        FileStream output,
        ZoneTreeStoreOptions options,
        long position,
        List<StorageMutation> batch,
        IncrementalHash digest) => WriteFrame(
            output,
            options,
            ZoneTreePersistenceFormat.CheckpointDataMagic,
            position,
            JsonDefaults.Serialize(batch),
            digest);

    private static void WriteFrame(
        FileStream output,
        ZoneTreeStoreOptions options,
        ulong magic,
        long position,
        byte[] payload,
        IncrementalHash? digest) => ZoneTreeCheckpointFrame.Write(output, options, magic, position, payload, digest);

    private static void SetPrivateMode(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
