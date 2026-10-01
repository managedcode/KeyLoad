using System.Buffers.Binary;
using System.Security.Cryptography;
using KeyLoad.Storage;
using ZoneTree;
using ZoneTree.Comparers;
using ZoneTree.Options;
using ZoneTree.Serializers;
using ZoneTree.WAL;

namespace KeyLoad.Storage.ZoneTree;

public enum CommitStage { HeaderWritten, PayloadWritten, JournalFlushed, MutationApplied, ApplyCompleted,
    SnapshotWritten, SnapshotFlushed, InstallPrepared, JournalSwapped }
public sealed record ZoneTreeStoreOptions(string Directory)
{
    public Guid? Incarnation { get; init; }
    public byte[]? SigningKey { get; init; }
    public Action<CommitStage, long, int>? FaultObserver { get; init; }
    public int MaxFrameBytes { get; init; } = 33_554_432;
    public long MaxSnapshotBytes { get; init; } = 4_294_967_296;
}

/// <summary>
/// The checksummed redo journal is canonical. ZoneTree is its ordered materialization.
/// The gate prevents readers from seeing a partially applied transaction. No network work runs inside it.
/// </summary>
public sealed partial class ZoneTreeStore : IAtomicStore, IKeyValueView
{
    private const ulong Magic = 0x314C4157444C4BUL;
    private const int HeaderLength = 52;
    private readonly ZoneTreeStoreOptions options;
    private readonly ReaderWriterLockSlim gate = new();
    private readonly FileStream ownership;
    private FileStream journal = null!;
    private IZoneTree<Memory<byte>, Memory<byte>> tree = null!;
    private IMaintainer maintainer = null!;
    private bool poisoned;
    private bool disposed;
    private long position;

    public StoreIdentity Identity { get; private set; }
    public long Position => Interlocked.Read(ref position);

    public ZoneTreeStore(ZoneTreeStoreOptions options)
    {
        this.options = options;
        if (!Directory.Exists(options.Directory) && !OperatingSystem.IsWindows())
            Directory.CreateDirectory(options.Directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else Directory.CreateDirectory(options.Directory);
        ownership = new FileStream(Path.Combine(options.Directory, "owner.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        try
        {
            var manifest = Path.Combine(options.Directory, "identity.json");
            Identity = File.Exists(manifest) ? ReadIdentity(manifest) : new(1, KeyCodec.Version, Guid.NewGuid(),
                options.Incarnation ?? Guid.NewGuid(), options.SigningKey?.ToArray() ?? RandomNumberGenerator.GetBytes(32),
                DurabilityProfile.ProcessDurable);
            if (Identity.FormatVersion is not (1 or 2) || Identity.KeyCodecVersion != KeyCodec.Version)
                throw Errors.Fail(ErrorCode.FormatUnsupported, "This database requires a different storage format.");
            if (Identity.SigningKey.Length != 32 || options.Incarnation is { } incarnation && incarnation != Identity.Incarnation
                || options.SigningKey is { } signingKey && !CryptographicOperations.FixedTimeEquals(signingKey, Identity.SigningKey))
                throw Errors.Fail(ErrorCode.TokenInvalidated, "The configured cluster identity does not match this database.");
            if (!File.Exists(manifest)) WriteIdentity(manifest, Identity);
            tree = OpenTree();
            journal = OpenJournal();
            Recover();
            maintainer = tree.CreateMaintainer();
            ReclaimInterruptedCheckpoints();
        }
        catch
        {
            try { maintainer?.Dispose(); }
            finally { try { journal?.Dispose(); } finally { try { tree?.Dispose(); } finally { ownership.Dispose(); } } }
            throw;
        }
    }
    private IZoneTree<Memory<byte>, Memory<byte>> OpenTree() => new ZoneTreeFactory<Memory<byte>, Memory<byte>>()
        .SetDataDirectory(Path.Combine(options.Directory, "tree")).SetComparer(new KeyComparer())
        .SetKeySerializer(new ByteArraySerializer()).SetValueSerializer(new ByteArraySerializer())
        .SetIsDeletedDelegate(static (in Memory<byte> key, in Memory<byte> value) => value.Span[0] == 0)
        .SetMarkValueDeletedDelegate(static (ref Memory<byte> value) => value = new byte[] { 0 })
        .ConfigureWriteAheadLogOptions(o => { o.WriteAheadLogMode = WriteAheadLogMode.Sync; o.CompressionMethod = CompressionMethod.None; })
        .ConfigureDiskSegmentOptions(o => o.CompressionMethod = CompressionMethod.None).OpenOrCreate();
    private FileStream OpenJournal() => new(Path.Combine(options.Directory, "commands.wal"), FileMode.OpenOrCreate,
        FileAccess.ReadWrite, FileShare.Read, 65_536, FileOptions.WriteThrough);

    private static StoreIdentity ReadIdentity(string path)
    {
        var envelope = JsonDefaults.Deserialize<IdentityEnvelope>(File.ReadAllBytes(path));
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(envelope.Payload), envelope.Checksum))
            throw Errors.Fail(ErrorCode.Corruption, "The database identity checksum is invalid.");
        return JsonDefaults.Deserialize<StoreIdentity>(envelope.Payload);
    }
    private static void WriteIdentity(string path, StoreIdentity identity)
    {
        var payload = JsonDefaults.Serialize(identity);
        var bytes = JsonDefaults.Serialize(new IdentityEnvelope(payload, SHA256.HashData(payload)));
        var temporary = path + ".tmp";
        using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4_096, FileOptions.WriteThrough))
        { file.Write(bytes); file.Flush(true); }
        File.Move(temporary, path, true);
    }
    private sealed record IdentityEnvelope(byte[] Payload, byte[] Checksum);

    private void Recover()
    {
        var header = new byte[HeaderLength];
        long validLength = 0;
        if (journal.Length >= HeaderLength)
        {
            journal.ReadExactly(header); journal.Position = 0;
            if (BinaryPrimitives.ReadUInt64LittleEndian(header) == CheckpointMagic)
            {
                var checkpoint = ReadCheckpoint(journal, Apply);
                position = checkpoint.Position;
                validLength = journal.Position;
            }
        }
        while (journal.Position < journal.Length)
        {
            var remaining = journal.Length - journal.Position;
            if (remaining < HeaderLength) { journal.SetLength(validLength); break; }
            journal.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt64LittleEndian(header) != Magic)
                throw Errors.Fail(ErrorCode.Corruption, "The redo journal contains an invalid frame header.");
            var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8));
            var sequence = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(12));
            if (length <= 0 || length > options.MaxFrameBytes || sequence != position + 1)
                throw Errors.Fail(ErrorCode.Corruption, "The redo journal has an invalid length or sequence.");
            if (journal.Length - journal.Position < length) { journal.SetLength(validLength); break; }
            var payload = new byte[length];
            journal.ReadExactly(payload);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), header.AsSpan(20, 32)))
                throw Errors.Fail(ErrorCode.Corruption, "The redo journal checksum is invalid.");
            var mutations = JsonDefaults.Deserialize<StorageMutation[]>(payload);
            foreach (var mutation in mutations) Apply(mutation);
            position = sequence;
            validLength = journal.Position;
        }
        journal.Position = journal.Length;
        journal.Flush(true);
    }

    private void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (poisoned) throw Errors.Fail(ErrorCode.RecoveryRequired, "The database must recover before accepting another operation.");
    }
    public T Read<T>(Func<IKeyValueView, T> read)
    {
        gate.EnterReadLock();
        try { Check(); return read(this); }
        finally { gate.ExitReadLock(); }
    }
    public T Commit<T>(Func<IAtomicTransaction, long, T> compile)
    {
        gate.EnterWriteLock();
        try
        {
            Check();
            var tx = new Transaction(this);
            var nextPosition = checked(position + 1);
            var result = compile(tx, nextPosition);
            var changes = tx.Changes.Select(p => new StorageMutation(p.Key, p.Value)).ToArray();
            if (changes.Length == 0) return result;
            var payload = JsonDefaults.Serialize(changes);
            if (payload.Length > options.MaxFrameBytes)
                throw Errors.Fail(ErrorCode.ResourceExhausted, "The compiled transaction exceeds the journal frame limit.");
            var header = new byte[HeaderLength];
            BinaryPrimitives.WriteUInt64LittleEndian(header, Magic);
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), payload.Length);
            BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(12), nextPosition);
            SHA256.HashData(payload, header.AsSpan(20));
            try
            {
                journal.Write(header);
                options.FaultObserver?.Invoke(CommitStage.HeaderWritten, nextPosition, 0);
                journal.Write(payload);
                options.FaultObserver?.Invoke(CommitStage.PayloadWritten, nextPosition, 0);
                journal.Flush(true);
                options.FaultObserver?.Invoke(CommitStage.JournalFlushed, nextPosition, 0);
                for (var i = 0; i < changes.Length; i++)
                {
                    Apply(changes[i]);
                    options.FaultObserver?.Invoke(CommitStage.MutationApplied, nextPosition, i);
                }
                position = nextPosition;
                options.FaultObserver?.Invoke(CommitStage.ApplyCompleted, nextPosition, changes.Length);
            }
            catch
            {
                poisoned = true;
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, "The transaction outcome is unknown. Recover and retry the same command ID.");
            }
            return result;
        }
        finally { gate.ExitWriteLock(); }
    }

    private void Apply(StorageMutation mutation)
    {
        Memory<byte> key = mutation.Key;
        if (mutation.Value is null) tree.ForceDelete(key);
        else
        {
            var value = new byte[mutation.Value.Length + 1];
            value[0] = 1;
            mutation.Value.CopyTo(value, 1);
            tree.Upsert(key, value);
        }
    }
    byte[]? IKeyValueView.Get(byte[] key) => Get(key);
    private byte[]? Get(byte[] key)
    {
        Memory<byte> memory = key;
        return tree.TryGet(memory, out var value) ? value.Span[1..].ToArray() : null;
    }
    ScanPage IKeyValueView.Scan(byte[] prefix, int maxRecords, byte[]? afterKey) => Scan(prefix, maxRecords, afterKey);
    private ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey)
    {
        if (maxRecords is < 1 or > 100_000) throw Errors.Fail(ErrorCode.BudgetExceeded, "The scan limit is outside its supported bounds.");
        using var iterator = tree.CreateIterator(IteratorType.NoRefresh);
        iterator.Seek(afterKey ?? prefix);
        var records = new List<KeyValueRecord>();
        long bytes = 0;
        while (iterator.Next())
        {
            var key = iterator.CurrentKey;
            if (!key.Span.StartsWith(prefix)) break;
            if (afterKey is not null && key.Span.SequenceCompareTo(afterKey) <= 0) continue;
            if (records.Count == maxRecords) return new(records.ToArray(), true);
            bytes += key.Length + iterator.CurrentValue.Length;
            if (bytes > 67_108_864) throw Errors.Fail(ErrorCode.BudgetExceeded, "The scan byte budget is exceeded.");
            records.Add(new(key.ToArray(), iterator.CurrentValue.Span[1..].ToArray()));
        }
        return new(records.ToArray(), false);
    }

    private sealed class Transaction(ZoneTreeStore store) : IAtomicTransaction
    {
        internal SortedDictionary<byte[], byte[]?> Changes { get; } = new(BinaryKeyComparer.Instance);
        public byte[]? Get(byte[] key) => Changes.TryGetValue(key, out var value) ? value?.ToArray() : store.Get(key);
        public void Put(byte[] key, byte[] value) => Changes[key.ToArray()] = value.ToArray();
        public void Delete(byte[] key) => Changes[key.ToArray()] = null;
        public void Reset() => Changes.Clear();
        public ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null)
        {
            var capacity = checked(maxRecords + Changes.Count + 1);
            var baseline = store.Scan(prefix, Math.Min(capacity, 100_000), afterKey);
            if (baseline.HasMore && capacity > 100_000) throw Errors.Fail(ErrorCode.BudgetExceeded, "The transactional scan exceeds its budget.");
            var merged = new SortedDictionary<byte[], byte[]>(BinaryKeyComparer.Instance);
            foreach (var item in baseline.Records) merged[item.Key] = item.Value;
            foreach (var change in Changes)
            {
                if (!change.Key.AsSpan().StartsWith(prefix) || afterKey is not null && BinaryKeyComparer.Instance.Compare(change.Key, afterKey) <= 0) continue;
                if (change.Value is null) merged.Remove(change.Key); else merged[change.Key] = change.Value;
            }
            return new(merged.Take(maxRecords).Select(p => new KeyValueRecord(p.Key.ToArray(), p.Value.ToArray())).ToArray(),
                merged.Count > maxRecords || baseline.HasMore);
        }
    }
    private sealed class KeyComparer : IRefComparer<Memory<byte>>
    {
        public int Compare(in Memory<byte> x, in Memory<byte> y) => x.Span.SequenceCompareTo(y.Span);
    }

    public void SetDispatchPaused(bool paused)
    {
        gate.EnterWriteLock();
        try { Check(); Identity = Identity with { DispatchPaused = paused }; WriteIdentity(Path.Combine(options.Directory, "identity.json"), Identity); }
        finally { gate.ExitWriteLock(); }
    }
    public long CreateBackup(string directory)
    {
        gate.EnterWriteLock();
        try
        {
            Check();
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
                throw Errors.Fail(ErrorCode.Conflict, "The backup destination must be empty.");
            CreatePrivateDirectory(directory);
            journal.Flush(true);
            var files = new List<BackupFile>();
            foreach (var name in new[] { "identity.json", "commands.wal" })
            {
                var source = Path.Combine(options.Directory, name);
                var destination = Path.Combine(directory, name);
                File.Copy(source, destination, false);
                using var copied = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                copied.Flush(true);
                copied.Position = 0;
                files.Add(new(name, copied.Length, Convert.ToHexStringLower(SHA256.HashData(copied))));
            }
            using var manifest = new FileStream(Path.Combine(directory, "backup.json"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
            manifest.Write(JsonDefaults.Serialize(new BackupManifest(1, Position, files.ToArray())));
            manifest.Flush(true);
            return Position;
        }
        finally { gate.ExitWriteLock(); }
    }
    public static StoreIdentity Restore(string backup, string destination, Guid? newIncarnation = null, byte[]? newSigningKey = null)
    {
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw Errors.Fail(ErrorCode.Conflict, "Restore requires an empty destination.");
        var manifest = JsonDefaults.Deserialize<BackupManifest>(File.ReadAllBytes(Path.Combine(backup, "backup.json")));
        if (manifest.Version != 1 || manifest.Files.Length != 2 || !manifest.Files.Select(f => f.Name).Order().SequenceEqual(new[] { "commands.wal", "identity.json" }))
            throw Errors.Fail(ErrorCode.FormatUnsupported, "The backup manifest is unsupported.");
        foreach (var item in manifest.Files)
        {
            var source = Path.Combine(backup, item.Name);
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw Errors.Fail(ErrorCode.Corruption, "Backup files cannot be links.");
            using var file = File.OpenRead(source);
            if (file.Length != item.Length || !string.Equals(Convert.ToHexStringLower(SHA256.HashData(file)), item.Checksum, StringComparison.Ordinal))
                throw Errors.Fail(ErrorCode.Corruption, "A backup file failed verification.");
        }
        var identity = ReadIdentity(Path.Combine(backup, "identity.json")) with
        {
            NodeId = Guid.NewGuid(), Incarnation = newIncarnation ?? Guid.NewGuid(),
            SigningKey = newSigningKey?.ToArray() ?? RandomNumberGenerator.GetBytes(32), DispatchPaused = true
        };
        CreatePrivateDirectory(destination);
        File.Copy(Path.Combine(backup, "commands.wal"), Path.Combine(destination, "commands.wal"));
        WriteIdentity(Path.Combine(destination, "identity.json"), identity);
        using (var restored = new ZoneTreeStore(new(destination)))
            restored.Commit((tx, _) =>
            {
                // Consensus history and routing restart in the new incarnation. Canonical domain data remains intact.
                tx.Delete(KeyCodec.Encode("system", "last-applied"));
                tx.Delete(KeyCodec.Encode("system", "clock"));
                tx.Delete(KeyCodec.Encode("membership", "orleans-membership"));
                tx.PutRecord(KeyCodec.Encode("system", "dispatch-paused"), true);
                return true;
            });
        return identity;
    }
    private sealed record BackupFile(string Name, long Length, string Checksum);
    private sealed record BackupManifest(int Version, long Position, BackupFile[] Files);
    private static void CreatePrivateDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    public void Dispose()
    {
        gate.EnterWriteLock();
        try
        {
            if (disposed) return;
            disposed = true;
            try { maintainer?.Dispose(); }
            finally
            {
                try { tree?.Dispose(); }
                finally { try { journal?.Dispose(); } finally { ownership.Dispose(); } }
            }
        }
        finally { gate.ExitWriteLock(); }
    }
}
