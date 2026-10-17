namespace KeyLoad.Storage;

/// <summary>Stages atomic writes while preserving the same gated read view.</summary>
public interface IAtomicTransaction : IKeyValueView
{
    /// <summary>Runs synchronous discard-only preparation over this transaction's staged image.</summary>
    /// <typeparam name="T">Independently owned result type.</typeparam>
    /// <param name="prepare">Callback whose child transaction and borrowed bytes cannot be retained.</param>
    /// <returns>The owned result; no child mutation is published to the parent or store.</returns>
    T Prepare<T>(Func<IAtomicTransaction, T> prepare);

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
