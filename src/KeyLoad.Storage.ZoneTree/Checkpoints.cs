using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using ZoneTree;

namespace KeyLoad.Storage.ZoneTree;

public sealed partial class ZoneTreeStore
{
    private const ulong CheckpointMagic = 0x32545043444C4BUL;
    private const ulong CheckpointDataMagic = 0x32415444444C4BUL;
    private const ulong CheckpointEndMagic = 0x32444E45444C4BUL;
    private sealed record CheckpointMetadata(int Version, int CodecVersion, Guid Incarnation, long Position, long AppliedPosition);
    private sealed record CheckpointFooter(long Records, string Checksum);

    public StorageSnapshot CreateSnapshot(string path, long? expectedAppliedPosition = null)
    {
        gate.EnterReadLock();
        try { Check(); return WriteCheckpoint(path, expectedAppliedPosition); }
        finally { gate.ExitReadLock(); }
    }
    private StorageSnapshot WriteCheckpoint(string path, long? expectedAppliedPosition)
    {
        var applied = Get(KeyCodec.Encode("system", "last-applied")) is { } value ? JsonDefaults.Deserialize<long>(value) : 0;
        if (expectedAppliedPosition is { } expected && applied != expected)
            throw Errors.Fail(ErrorCode.OwnershipLost, "The snapshot no longer has the requested committed cut.");
        var metadata = new CheckpointMetadata(2, KeyCodec.Version, Identity.Incarnation, Position, applied);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65_536, FileOptions.WriteThrough);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        WriteCheckpointFrame(output, CheckpointMagic, metadata.Position, JsonDefaults.Serialize(metadata), digest);
        var batch = new List<StorageMutation>(); long batchBytes = 0, records = 0;
        using (var iterator = tree.CreateIterator(IteratorType.NoRefresh))
        {
            while (iterator.Next())
            {
                var item = new StorageMutation(iterator.CurrentKey.ToArray(), iterator.CurrentValue.Span[1..].ToArray());
                batch.Add(item); batchBytes += item.Key.Length + item.Value!.Length; records++;
                if (batchBytes >= 4_194_304 || batch.Count >= 1_000)
                {
                    WriteCheckpointFrame(output, CheckpointDataMagic, metadata.Position, JsonDefaults.Serialize(batch), digest);
                    batch.Clear(); batchBytes = 0;
                }
            }
        }
        if (batch.Count != 0) WriteCheckpointFrame(output, CheckpointDataMagic, metadata.Position, JsonDefaults.Serialize(batch), digest);
        var footer = new CheckpointFooter(records, Convert.ToHexStringLower(digest.GetHashAndReset()));
        WriteCheckpointFrame(output, CheckpointEndMagic, metadata.Position, JsonDefaults.Serialize(footer), null);
        options.FaultObserver?.Invoke(CommitStage.SnapshotWritten, metadata.Position, 0);
        output.Flush(true);
        options.FaultObserver?.Invoke(CommitStage.SnapshotFlushed, metadata.Position, 0);
        return new(metadata.Incarnation, metadata.Position, metadata.AppliedPosition, records);
    }
    private void WriteCheckpointFrame(FileStream output, ulong magic, long sequence, byte[] payload, IncrementalHash? digest)
    {
        if (payload.Length > options.MaxFrameBytes || output.Position + HeaderLength + payload.Length > options.MaxSnapshotBytes)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The snapshot exceeds its frame or byte budget.");
        var header = new byte[HeaderLength];
        BinaryPrimitives.WriteUInt64LittleEndian(header, magic);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), payload.Length);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(12), sequence);
        SHA256.HashData(payload, header.AsSpan(20));
        output.Write(header); output.Write(payload);
        digest?.AppendData(header); digest?.AppendData(payload);
    }
    private StorageSnapshot ReadCheckpoint(FileStream input, Action<StorageMutation>? apply = null)
    {
        if (input.Length > options.MaxSnapshotBytes && apply is null)
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The snapshot exceeds its byte budget.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        CheckpointMetadata? metadata = null; long count = 0, applied = 0; byte[]? previousKey = null;
        var appliedKey = KeyCodec.Encode("system", "last-applied");
        while (true)
        {
            if (input.Length - input.Position < HeaderLength) throw Errors.Fail(ErrorCode.Corruption, "The checkpoint is incomplete.");
            var header = new byte[HeaderLength]; input.ReadExactly(header);
            var magic = BinaryPrimitives.ReadUInt64LittleEndian(header);
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            var position = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(12));
            if (length <= 0 || length > options.MaxFrameBytes || input.Length - input.Position < length)
                throw Errors.Fail(ErrorCode.Corruption, "A checkpoint frame has an invalid length.");
            var payload = new byte[length]; input.ReadExactly(payload);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), header.AsSpan(20, 32)))
                throw Errors.Fail(ErrorCode.Corruption, "A checkpoint frame failed checksum verification.");
            if (metadata is null)
            {
                if (magic != CheckpointMagic) throw Errors.Fail(ErrorCode.FormatUnsupported, "The snapshot is not a supported checkpoint.");
                metadata = JsonDefaults.Deserialize<CheckpointMetadata>(payload);
                if (metadata.Version != 2 || metadata.CodecVersion != KeyCodec.Version)
                    throw Errors.Fail(ErrorCode.FormatUnsupported, "The snapshot format is unsupported.");
                if (metadata.Position < 0 || metadata.AppliedPosition < 0 || metadata.Incarnation == Guid.Empty || position != metadata.Position)
                    throw Errors.Fail(ErrorCode.Corruption, "The checkpoint cut is invalid.");
            }
            else
            {
                if (position != metadata.Position) throw Errors.Fail(ErrorCode.Corruption, "Checkpoint frames have different cuts.");
                if (magic == CheckpointEndMagic)
                {
                    var footer = JsonDefaults.Deserialize<CheckpointFooter>(payload);
                    if (footer.Records != count || metadata.AppliedPosition != applied
                        || footer.Checksum != Convert.ToHexStringLower(digest.GetHashAndReset()))
                        throw Errors.Fail(ErrorCode.Corruption, "The complete checkpoint failed verification.");
                    return new(metadata.Incarnation, metadata.Position, metadata.AppliedPosition, count);
                }
                if (magic != CheckpointDataMagic) throw Errors.Fail(ErrorCode.Corruption, "The checkpoint contains an invalid frame type.");
                foreach (var mutation in JsonDefaults.Deserialize<StorageMutation[]>(payload))
                {
                    if (mutation.Key.Length == 0 || mutation.Value is null || previousKey is not null
                        && BinaryKeyComparer.Instance.Compare(previousKey, mutation.Key) >= 0)
                        throw Errors.Fail(ErrorCode.Corruption, "Checkpoint keys are not strictly ordered live records.");
                    previousKey = mutation.Key;
                    if (mutation.Key.AsSpan().SequenceEqual(appliedKey)) applied = JsonDefaults.Deserialize<long>(mutation.Value);
                    count++; apply?.Invoke(mutation);
                }
            }
            digest.AppendData(header); digest.AppendData(payload);
        }
    }
    public StorageSnapshot Compact()
    {
        gate.EnterWriteLock();
        var temporary = Path.Combine(options.Directory, "checkpoint-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Check();
            var snapshot = WriteCheckpoint(temporary, null);
            using (var verify = File.OpenRead(temporary)) ReadCheckpoint(verify);
            ReplaceJournal(temporary, snapshot.Position, replaceTree: false);
            return snapshot;
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } finally { gate.ExitWriteLock(); } }
    }
    public StorageSnapshot InstallSnapshot(string path, long expectedAppliedPosition)
    {
        // Validate the complete transfer before changing any live state or closing the old generation.
        var metadata = VerifySnapshot(path);
        if (metadata.Incarnation != Identity.Incarnation || metadata.AppliedPosition != expectedAppliedPosition)
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The snapshot belongs to a different cluster or committed cut.");
        gate.EnterWriteLock();
        var temporary = Path.Combine(options.Directory, "install-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Check();
            var currentApplied = Get(KeyCodec.Encode("system", "last-applied")) is { } value ? JsonDefaults.Deserialize<long>(value) : 0;
            if (expectedAppliedPosition < currentApplied) throw Errors.Fail(ErrorCode.OwnershipLost, "A stale snapshot cannot replace a newer committed state.");
            File.Copy(path, temporary, false);
            StorageSnapshot snapshot;
            using (var input = new FileStream(temporary, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                snapshot = ReadCheckpoint(input);
                if (snapshot.Incarnation != Identity.Incarnation || snapshot.AppliedPosition != expectedAppliedPosition || input.Position != input.Length)
                    throw Errors.Fail(ErrorCode.TokenInvalidated, "The staged snapshot scope is invalid.");
                options.FaultObserver?.Invoke(CommitStage.SnapshotWritten, snapshot.Position, 0);
                input.Flush(true);
                options.FaultObserver?.Invoke(CommitStage.SnapshotFlushed, snapshot.Position, 0);
            }
            ReplaceJournal(temporary, snapshot.Position, replaceTree: true);
            return snapshot;
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } finally { gate.ExitWriteLock(); } }
    }
    public StorageSnapshot VerifySnapshot(string path)
    {
        using var input = File.OpenRead(path);
        var snapshot = ReadCheckpoint(input);
        if (input.Position != input.Length) throw Errors.Fail(ErrorCode.Corruption, "The snapshot contains data beyond its verified cut.");
        return snapshot;
    }
    private void ReclaimInterruptedCheckpoints()
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(options.Directory))
        {
            var name = Path.GetFileName(path);
            var retired = name.StartsWith("tree-retired-", StringComparison.Ordinal) && Guid.TryParseExact(name[13..], "N", out _);
            var temporary = (name.StartsWith("checkpoint-", StringComparison.Ordinal) || name.StartsWith("install-", StringComparison.Ordinal))
                && name.EndsWith(".tmp", StringComparison.Ordinal) && Guid.TryParseExact(name[(name.IndexOf('-') + 1)..^4], "N", out _);
            if (!retired && !temporary) continue;
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                if (retired) Directory.Delete(path, true); else File.Delete(path);
            }
            catch (IOException) { /* The verified canonical journal is already open; locked derived files can wait for the next reclamation. */ }
        }
    }
    private void ReplaceJournal(string temporary, long cut, bool replaceTree)
    {
        string? retiredTree = null;
        try
        {
            Identity = Identity with { FormatVersion = 2, ReadGeneration = replaceTree ? checked(Identity.ReadGeneration + 1) : Identity.ReadGeneration };
            WriteIdentity(Path.Combine(options.Directory, "identity.json"), Identity);
            journal.Flush(true);
            if (replaceTree)
            {
                maintainer.Dispose(); tree.Dispose();
                retiredTree = Path.Combine(options.Directory, "tree-retired-" + Guid.NewGuid().ToString("N"));
                Directory.Move(Path.Combine(options.Directory, "tree"), retiredTree);
            }
            options.FaultObserver?.Invoke(CommitStage.InstallPrepared, cut, 0);
            journal.Dispose();
            File.Move(temporary, Path.Combine(options.Directory, "commands.wal"), true);
            options.FaultObserver?.Invoke(CommitStage.JournalSwapped, cut, 0);
            journal = OpenJournal();
            if (replaceTree)
            {
                tree = OpenTree(); position = 0; Recover(); maintainer = tree.CreateMaintainer();
            }
            else { journal.Position = journal.Length; position = cut; }
        }
        catch
        {
            poisoned = true;
            throw Errors.Fail(ErrorCode.RecoveryRequired, "Snapshot installation was interrupted. Recover before resuming service.");
        }
        if (retiredTree is not null)
        {
            try { Directory.Delete(retiredTree, true); }
            catch (IOException) { /* Derived files can be reclaimed after handle cleanup; the new journal is authoritative. */ }
        }
    }
}
