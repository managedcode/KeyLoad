namespace KeyLoad.Storage;

/// <summary>Serializes typed records through the canonical gated storage contract.</summary>
public static class StorageRecords
{
    /// <summary>Decodes a typed record from borrowed storage bytes without a raw copy.</summary>
    /// <typeparam name="T">Owned reference-type record.</typeparam>
    /// <param name="view">View valid only inside its owning store action.</param>
    /// <param name="key">Encoded record key.</param>
    /// <param name="observer">Optional actual key/value work charge before decoding, including missing lookups.</param>
    /// <returns>The decoded owned record, or null when no value is stored.</returns>
    public static T? GetRecord<T>(this IKeyValueView view, byte[] key, StorageReadObserver? observer = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(key);
        T? record = null;
        view.ReadValue(key, value => record = OrleansSerialization.Deserialize<T>(value), observer);
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
        tx.Put(key, OrleansSerialization.Serialize(value));
    }
}
