using System.Collections.Immutable;

namespace KeyLoad.Storage;

/// <summary>Owns one independent key and value copied from a gated storage view.</summary>
/// <param name="Key">Encoded record key owned by this result.</param>
/// <param name="Value">Logical record bytes owned by this result.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.KeyValueRecord)]
public sealed record KeyValueRecord([property: Orleans.Id(0)] ReadOnlyMemory<byte> Key, [property: Orleans.Id(1)] ReadOnlyMemory<byte> Value);
/// <summary>Describes an ordered atomic record write or tombstone.</summary>
/// <param name="Key">Encoded record key included in the commit.</param>
/// <param name="Value">New logical bytes, or null to delete the key.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StorageMutation)]
public sealed record StorageMutation([property: Orleans.Id(0)] ReadOnlyMemory<byte> Key, [property: Orleans.Id(1)] ReadOnlyMemory<byte>? Value);
/// <summary>Owns a bounded ordered page and its logical limit-lookahead result.</summary>
/// <param name="Records">Independent key/value copies in encoded key order.</param>
/// <param name="HasMore">True when another live matching record was examined.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ScanPage)]
public sealed record ScanPage([property: Orleans.Id(0)] ImmutableArray<KeyValueRecord> Records, [property: Orleans.Id(1)] bool HasMore);
/// <summary>Identifies the verified store cut represented by a snapshot.</summary>
/// <param name="Incarnation">Storage incarnation recorded in the snapshot.</param>
/// <param name="Position">Local durable storage position of the captured cut.</param>
/// <param name="AppliedPosition">Replicated operation position captured in the cut.</param>
/// <param name="RecordCount">Number of logical records included in the snapshot.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StorageSnapshot)]
public sealed record StorageSnapshot([property: Orleans.Id(0)] Guid Incarnation, [property: Orleans.Id(1)] long Position, [property: Orleans.Id(2)] long AppliedPosition, [property: Orleans.Id(3)] long RecordCount);
/// <summary>Reads a consistent view only inside its owning store action.</summary>
public interface IKeyValueView
{
    /// <summary>Copies a found record into an independently owned buffer.</summary>
    /// <param name="key">Encoded lookup key.</param>
    /// <returns>Owned logical bytes, or null when the key is absent or deleted.</returns>
    byte[]? ReadOwnedValue(byte[] key);
    /// <summary>Copies a bounded ordered page of matching logical records.</summary>
    /// <param name="prefix">Required encoded key prefix.</param>
    /// <param name="maxRecords">Maximum live records delivered before lookahead.</param>
    /// <param name="afterKey">Optional exclusive lower encoded key bound.</param>
    /// <returns>Owned key/value copies and whether another live record exists.</returns>
    ScanPage Scan(byte[] prefix, int maxRecords, byte[]? afterKey = null);

    /// <summary>Reads borrowed bytes inside the current storage gate, without an intermediate value copy.</summary>
    /// <param name="key">Owned lookup key supplied by the caller.</param>
    /// <param name="reader">Callback that cannot retain the borrowed span.</param>
    /// <param name="observer">Optional work charge before invoking the reader, including missing lookups.</param>
    /// <returns>True when a stored value was found.</returns>
    bool ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer = null);

    /// <summary>Visits ordered records inside the current storage gate and checks work before copying.</summary>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum logical records delivered before lookahead.</param>
    /// <param name="visitor">Read-only borrowed-record callback; false stops before another advance. Do not mutate the transaction from this callback.</param>
    /// <param name="afterKey">Optional exclusive lower key bound.</param>
    /// <param name="untilKey">Optional exclusive upper key bound.</param>
    /// <param name="observer">Optional examined-byte charge before callback or lookahead.</param>
    /// <param name="cancellationToken">Cancellation checked during traversal.</param>
    /// <returns>Delivered record count, stop reason and examined byte count.</returns>
    StorageScanResult VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default);

    /// <summary>Visits descending encoded keys inside the same storage gate without materializing the range.</summary>
    /// <param name="prefix">Required key prefix.</param>
    /// <param name="maxRecords">Maximum live records delivered before charged lookahead.</param>
    /// <param name="visitor">Read-only borrowed callback; false stops before another advance. Do not mutate the transaction.</param>
    /// <param name="afterKey">Optional exclusive lower key bound, independent of direction.</param>
    /// <param name="untilKey">Optional exclusive upper key bound, independent of direction.</param>
    /// <param name="observer">Optional examined-byte charge before callback or lookahead.</param>
    /// <param name="cancellationToken">Cancellation checked during traversal.</param>
    /// <returns>Delivered count, stop reason and examined logical bytes.</returns>
    StorageScanResult VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey = null, byte[]? untilKey = null, StorageReadObserver? observer = null,
        CancellationToken cancellationToken = default);
}
/// <summary>Stages atomic writes while preserving the same gated read view.</summary>
public interface IAtomicTransaction : IKeyValueView
{
    /// <summary>Stages a key/value write using provider-owned copies.</summary>
    /// <param name="key">Encoded key to insert or replace.</param>
    /// <param name="value">Logical bytes to persist.</param>
    void Put(byte[] key, byte[] value);
    /// <summary>Stages a tombstone for an encoded key.</summary>
    /// <param name="key">Encoded key to delete.</param>
    void Delete(byte[] key);
    /// <summary>Discards this action's staged writes without changing committed data.</summary>
    void Reset();
    /// <summary>Validates the complete staged commit before any durable write.</summary>
    /// <remarks>Callers may reset rejected effects and persist a bounded rejection outcome.</remarks>
    void ValidateCommit();
}
/// <summary>Records the persisted format, authority and token-signing identity of a store.</summary>
/// <param name="FormatVersion">Persisted storage format version.</param>
/// <param name="KeyCodecVersion">Encoded key format version.</param>
/// <param name="NodeId">Physical node identity owning this store.</param>
/// <param name="Incarnation">Authority incarnation used to invalidate stale tokens.</param>
/// <param name="SigningKey">Private key material used by server-owned token signing.</param>
/// <param name="Durability">Acknowledgement guarantee supplied by the provider.</param>
/// <param name="DispatchPaused">Whether restored dispatch remains paused.</param>
/// <param name="ReadGeneration">Generation fencing restored or replaced read authority.</param>
/// <param name="MinimumReaderContract">Required reader capability persisted before native journal adoption.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StoreIdentity)]
public sealed record StoreIdentity([property: Orleans.Id(0)] int FormatVersion, [property: Orleans.Id(1)] int KeyCodecVersion, [property: Orleans.Id(2)] Guid NodeId, [property: Orleans.Id(3)] Guid Incarnation,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> SigningKey, [property: Orleans.Id(5)] DurabilityProfile Durability, [property: Orleans.Id(6)] bool DispatchPaused = false, [property: Orleans.Id(7)] long ReadGeneration = StoreIdentity.DefaultReadGeneration,
    [property: Orleans.Id(8), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int MinimumReaderContract = StoreReaderContract.Legacy)
{
    private const int DefaultReadGeneration = 0;
}
/// <summary>Owns durable records, store gates and snapshot/backup publication.</summary>
/// <remarks>
/// Inside a read callback, Position and the scalar Identity fields describe the same protected cut as the view.
/// Effective record publications advance Position. Replacement which can reuse a position must advance a nonreused
/// ReadGeneration or change the incarnation or node identity before another read. Uncertain or failed live journal/tree
/// publication or live replacement must reject subsequent reads until recovery.
/// Pure validation, compile or snapshot verification rejection before publication preserves the unchanged healthy cut.
/// Node-local replica term observations depend on these provider guarantees.
/// </remarks>
public interface IAtomicStore : IDisposable
{
    /// <summary>Gets the current persisted store and token-signing identity.</summary>
    StoreIdentity Identity { get; }
    /// <summary>Gets the last published local storage commit position.</summary>
    long Position { get; }
    /// <summary>Runs a synchronous read while its consistent storage gate is held.</summary>
    /// <typeparam name="T">Owned result type.</typeparam>
    /// <param name="read">Action that must not retain the view or borrowed bytes.</param>
    /// <returns>The action's independently owned result.</returns>
    T Read<T>(Func<IKeyValueView, T> read);
    /// <summary>Compiles and durably publishes one atomic set of staged writes.</summary>
    /// <typeparam name="T">Owned committed result type.</typeparam>
    /// <param name="compile">Action receiving the gated transaction and proposed local position.</param>
    /// <returns>The result after successful commit publication.</returns>
    T Commit<T>(Func<IAtomicTransaction, long, T> compile);
    /// <summary>Persists the store's dispatch pause state.</summary>
    /// <param name="paused">Requested pause state.</param>
    void SetDispatchPaused(bool paused);
    /// <summary>Creates the provider's verified backup in the destination directory.</summary>
    /// <param name="directory">Destination directory owned by the backup caller.</param>
    /// <returns>The captured local storage position.</returns>
    long CreateBackup(string directory);
    /// <summary>Publishes a snapshot of a verified committed store cut.</summary>
    /// <param name="path">Destination snapshot path.</param>
    /// <param name="expectedAppliedPosition">Optional exact replicated cut required by the caller.</param>
    /// <returns>The published snapshot metadata.</returns>
    StorageSnapshot CreateSnapshot(string path, long? expectedAppliedPosition = null);
    /// <summary>Validates an existing snapshot without installing it.</summary>
    /// <param name="path">Snapshot to verify.</param>
    /// <returns>Metadata from the verified snapshot.</returns>
    StorageSnapshot VerifySnapshot(string path);
    /// <summary>Installs a verified snapshot at the required replicated cut.</summary>
    /// <param name="path">Snapshot to install.</param>
    /// <param name="expectedAppliedPosition">Exact replicated position required by the installation.</param>
    /// <returns>The installed snapshot metadata.</returns>
    StorageSnapshot InstallSnapshot(string path, long expectedAppliedPosition);
    /// <summary>Publishes a compacted store cut while preserving logical records.</summary>
    /// <returns>The compacted snapshot metadata.</returns>
    StorageSnapshot Compact();
}
/// <summary>Serializes typed records through the canonical gated storage contract.</summary>
public static class StorageRecords
{
    /// <summary>Decodes a typed record from borrowed storage bytes without a raw copy.</summary>
    /// <typeparam name="T">Owned reference-type record.</typeparam>
    /// <param name="view">View valid only inside its owning store action.</param>
    /// <param name="key">Encoded record key.</param>
    /// <returns>The decoded owned record, or null when no value is stored.</returns>
    public static T? GetRecord<T>(this IKeyValueView view, byte[] key) where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        T? record = null;
        view.ReadValue(key, value => record = NativeSerialization.Deserialize<T>(value));
        return record;
    }
    /// <summary>Serializes and stages one typed value through the provider-owned write.</summary>
    /// <typeparam name="T">Value type serialized with canonical KeyLoad options.</typeparam>
    /// <param name="tx">Transaction valid only inside its owning commit action.</param>
    /// <param name="key">Encoded record key.</param>
    /// <param name="value">Value to serialize, including null when the value type allows it.</param>
    public static void PutRecord<T>(this IAtomicTransaction tx, byte[] key, T value)
    {
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(key);
        tx.Put(key, NativeSerialization.Serialize(value));
    }
}
