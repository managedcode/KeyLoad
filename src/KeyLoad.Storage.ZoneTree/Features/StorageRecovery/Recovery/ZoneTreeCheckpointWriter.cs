using System.Security.Cryptography;
using ZoneTree;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointWriter
{
    private const int InitialSnapshotPosition = 0;
    private const int InitialRecordCount = 0;
    private const int ObserverNonMutationIndex = 0;
    private const int EmptyBatchBytes = 0;
    private const int EmptyBatchCount = 0;
    private const int SingleRecordIncrement = 1;

    internal static StorageSnapshot WriteMutations(FileStream output, ZoneTreeStoreOptions options,
        StorageSnapshot snapshot, Action<Action<StorageMutation>> visit)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(visit);
        if (snapshot.Incarnation == Guid.Empty || snapshot.Position < InitialSnapshotPosition || snapshot.AppliedPosition < InitialSnapshotPosition
            || snapshot.RecordCount < InitialRecordCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointCutInvalid);
        }

        var metadata = new ZoneTreeCheckpointMetadata(ZoneTreePersistenceFormat.CheckpointVersion,
            KeyCodec.Version, snapshot.Incarnation, snapshot.Position, snapshot.AppliedPosition);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointMagic, metadata.Position,
            NativeSerialization.Serialize(metadata), digest);
        var records = WriteMutations(output, options, metadata.Position, visit, digest);
        if (records != snapshot.RecordCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        }

        var footer = new ZoneTreeCheckpointFooter(records, Convert.ToHexStringLower(digest.GetHashAndReset()));
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointEndMagic, metadata.Position,
            NativeSerialization.Serialize(footer), null);
        options.FaultObserver?.Invoke(CommitStage.SnapshotWritten, metadata.Position, ObserverNonMutationIndex);
        output.Flush(true);
        options.FaultObserver?.Invoke(CommitStage.SnapshotFlushed, metadata.Position, ObserverNonMutationIndex);
        return snapshot;
    }

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
            NativeSerialization.Serialize(metadata), digest);
        var records = WriteRecords(output, options, metadata.Position, tree, digest);
        var footer = new ZoneTreeCheckpointFooter(records, Convert.ToHexStringLower(digest.GetHashAndReset()));
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointEndMagic, metadata.Position,
            NativeSerialization.Serialize(footer), null);
        options.FaultObserver?.Invoke(CommitStage.SnapshotWritten, metadata.Position, ObserverNonMutationIndex);
        output.Flush(true);
        options.FaultObserver?.Invoke(CommitStage.SnapshotFlushed, metadata.Position, ObserverNonMutationIndex);
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
        long batchBytes = EmptyBatchBytes;
        long records = InitialRecordCount;
        using (var iterator = tree.CreateIterator(IteratorType.NoRefresh))
        {
            while (iterator.Next())
            {
                var item = new StorageMutation(iterator.CurrentKey.ToArray(), iterator.CurrentValue.Span[
                    ZoneTreePersistenceFormat.StorageValueHeaderBytes..].ToArray());
                batch.Add(item);
                batchBytes += item.Key.Length + item.Value!.Value.Length;
                records++;
                if (batchBytes >= options.CheckpointBatchBytes
                    || batch.Count >= options.CheckpointBatchRecords)
                {
                    WriteBatch(output, options, position, batch, digest);
                    batch.Clear();
                    batchBytes = EmptyBatchBytes;
                }
            }
        }

        if (batch.Count != EmptyBatchCount)
        {
            WriteBatch(output, options, position, batch, digest);
        }

        return records;
    }

    private static long WriteMutations(FileStream output, ZoneTreeStoreOptions options, long position,
        Action<Action<StorageMutation>> visit, IncrementalHash digest)
    {
        var batch = new List<StorageMutation>();
        long batchBytes = EmptyBatchBytes;
        long records = InitialRecordCount;
        visit(mutation =>
        {
            if (mutation is null || mutation.Key.IsEmpty || mutation.Value is not { } value)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid);
            }

            var copy = new StorageMutation(mutation.Key.ToArray(), value.ToArray());
            batch.Add(copy);
            batchBytes = checked(batchBytes + copy.Key.Length + copy.Value!.Value.Length);
            records = checked(records + SingleRecordIncrement);
            if (batchBytes >= options.CheckpointBatchBytes
                || batch.Count >= options.CheckpointBatchRecords)
            {
                WriteBatch(output, options, position, batch, digest);
                batch.Clear();
                batchBytes = EmptyBatchBytes;
            }
        });

        if (batch.Count != EmptyBatchCount)
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
            NativeSerialization.Serialize(batch.ToArray()),
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
