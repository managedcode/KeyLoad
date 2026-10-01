namespace KeyLoad.Storage;

public sealed record KeyValueRecord(byte[] Key, byte[] Value);
public sealed record StorageMutation(byte[] Key, byte[]? Value);
public sealed record ScanPage(KeyValueRecord[] Records, bool HasMore);
public sealed record StorageSnapshot(Guid Incarnation, long Position, long AppliedPosition, long RecordCount);
public interface IKeyValueView
{
    byte[]? Get(byte[] key);
    ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null);
}
public interface IAtomicTransaction : IKeyValueView
{
    void Put(byte[] key, byte[] value);
    void Delete(byte[] key);
    void Reset();
    // Throws before any durable write; callers can roll back staged effects and persist a bounded rejection outcome.
    void ValidateCommit();
}
public sealed record StoreIdentity(int FormatVersion, int KeyCodecVersion, Guid NodeId, Guid Incarnation,
    byte[] SigningKey, DurabilityProfile Durability, bool DispatchPaused = false, long ReadGeneration = 0);
public interface IAtomicStore : IDisposable
{
    StoreIdentity Identity { get; }
    long Position { get; }
    T Read<T>(Func<IKeyValueView, T> read);
    T Commit<T>(Func<IAtomicTransaction, long, T> compile);
    void SetDispatchPaused(bool paused);
    long CreateBackup(string directory);
    StorageSnapshot CreateSnapshot(string path, long? expectedAppliedPosition = null);
    StorageSnapshot VerifySnapshot(string path);
    StorageSnapshot InstallSnapshot(string path, long expectedAppliedPosition);
    StorageSnapshot Compact();
}
public static class StorageRecords
{
    public static T? GetRecord<T>(this IKeyValueView view, byte[] key) where T : class
        => view.Get(key) is { } bytes ? JsonDefaults.Deserialize<T>(bytes) : null;
    public static void PutRecord<T>(this IAtomicTransaction tx, byte[] key, T value) => tx.Put(key, JsonDefaults.Serialize(value));
}
