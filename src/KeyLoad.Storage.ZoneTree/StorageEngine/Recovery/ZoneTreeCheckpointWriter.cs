using System.Security.Cryptography;
using ZoneTree;

namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeCheckpointWriter
{
    internal static StorageSnapshot WriteMutations(FileStream output, ZoneTreeStoreOptions options,
        StorageSnapshot snapshot, Action<Action<StorageMutation>> visit)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(visit);
        ZoneTreeCheckpointPublication.RequireStream(output);
        if (snapshot.Incarnation == Guid.Empty || snapshot.Position < 0 || snapshot.AppliedPosition < 0
            || snapshot.RecordCount < 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointCutInvalid);
        }

        var budget = new ZoneTreeCheckpointBudget(options, TimeProvider.System, CancellationToken.None);
        var metadata = new ZoneTreeCheckpointMetadata(KeyCodec.Version,
            snapshot.Incarnation, snapshot.Position, snapshot.AppliedPosition);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointMagic, metadata.Position,
            OrleansSerialization.Serialize(metadata), digest);
        var records = WriteMutations(output, options, metadata.Position, visit, digest, budget);
        if (records != snapshot.RecordCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointVerificationFailed);
        }

        var footer = new ZoneTreeCheckpointFooter(records, Convert.ToHexStringLower(digest.GetHashAndReset()));
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointEndMagic, metadata.Position,
            OrleansSerialization.Serialize(footer), null);
        options.FaultObserver?.Invoke(CommitStage.SnapshotWritten, metadata.Position, 0);
        output.Flush(true);
        options.FaultObserver?.Invoke(CommitStage.SnapshotFlushed, metadata.Position, 0);
        ZoneTreeCheckpointPublication.RequireReplayable(output, options, work: budget);
        return snapshot;
    }

    internal static StorageSnapshot Write(
        string path,
        ZoneTreeStoreOptions options,
        StoreIdentity identity,
        long position,
        long appliedPosition,
        ITransactionalZoneTree<Memory<byte>, Memory<byte>> tree)
    {
        var budget = new ZoneTreeCheckpointBudget(options, TimeProvider.System, CancellationToken.None);
        var metadata = new ZoneTreeCheckpointMetadata(KeyCodec.Version,
            identity.Incarnation, position, appliedPosition);
        using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            options.FileBufferBytes,
            FileOptions.WriteThrough);
        SetPrivateMode(path);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointMagic, metadata.Position,
            OrleansSerialization.Serialize(metadata), digest);
        var records = WriteRecords(output, options, metadata.Position, tree, digest, budget);
        var footer = new ZoneTreeCheckpointFooter(records, Convert.ToHexStringLower(digest.GetHashAndReset()));
        WriteFrame(output, options, ZoneTreePersistenceFormat.CheckpointEndMagic, metadata.Position,
            OrleansSerialization.Serialize(footer), null);
        options.FaultObserver?.Invoke(CommitStage.SnapshotWritten, metadata.Position, 0);
        output.Flush(true);
        options.FaultObserver?.Invoke(CommitStage.SnapshotFlushed, metadata.Position, 0);
        ZoneTreeCheckpointPublication.RequireReplayable(output, options, work: budget);
        return new(metadata.Incarnation, metadata.Position, metadata.AppliedPosition, records);
    }

    private static long WriteRecords(
        FileStream output,
        ZoneTreeStoreOptions options,
        long position,
        ITransactionalZoneTree<Memory<byte>, Memory<byte>> tree,
        IncrementalHash digest, ZoneTreeCheckpointBudget budget)
    {
        var batch = new List<StorageMutation>();
        long batchBytes = 0;
        long records = 0;
        using var snapshot = tree.BeginReadOnlyTransaction();
        using (var iterator = snapshot.CreateIterator())
        {
            while (iterator.Next())
            {
                var item = new StorageMutation(iterator.CurrentKey.ToArray(), iterator.CurrentValue.ToArray());
                budget.Charge(checked((long)item.Key.Length + item.Value!.Value.Length));
                batch.Add(item);
                batchBytes += item.Key.Length + item.Value!.Value.Length;
                records++;
                if (batchBytes >= options.CheckpointBatchBytes
                    || batch.Count >= options.CheckpointBatchRecords)
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

    private static long WriteMutations(FileStream output, ZoneTreeStoreOptions options, long position,
        Action<Action<StorageMutation>> visit, IncrementalHash digest, ZoneTreeCheckpointBudget budget)
    {
        var batch = new List<StorageMutation>();
        long batchBytes = 0;
        long records = 0;
        visit(mutation =>
        {
            if (mutation is null || mutation.Key.IsEmpty || mutation.Value is not { } value)
            {
                throw Errors.Fail(ErrorCode.Corruption, ZoneTreePersistenceFormat.CheckpointRecordsInvalid);
            }

            budget.Charge(checked((long)mutation.Key.Length + value.Length));
            var copy = new StorageMutation(mutation.Key.ToArray(), value.ToArray());
            batch.Add(copy);
            batchBytes = checked(batchBytes + copy.Key.Length + copy.Value!.Value.Length);
            records = checked(records + 1);
            if (batchBytes >= options.CheckpointBatchBytes
                || batch.Count >= options.CheckpointBatchRecords)
            {
                WriteBatch(output, options, position, batch, digest);
                batch.Clear();
                batchBytes = 0;
            }
        });

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
            OrleansSerialization.Serialize(batch.ToArray()),
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
