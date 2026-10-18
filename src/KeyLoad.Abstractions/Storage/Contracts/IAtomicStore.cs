namespace KeyLoad.Storage;

/// <summary>Owns durable records, store gates and snapshot/backup publication.</summary>
/// <remarks>
/// Inside a read callback, Position and the scalar Identity fields describe the same protected cut as the view.
/// Effective record publications advance Position. Replacement which can reuse a position must advance a nonreused
/// ReadGeneration or change the incarnation or node identity before another read. Uncertain or failed live journal/tree
/// publication or live replacement must reject subsequent reads until recovery.
/// Pure validation, compile or snapshot verification rejection before publication preserves the unchanged healthy cut.
/// Canonical read cuts and persisted copy acknowledgements depend on these provider guarantees.
/// </remarks>
public interface IAtomicStore : IDisposable
{
    /// <summary>Gets the current persisted store and token-signing identity.</summary>
    StoreIdentity Identity
    {
        get;
    }
    /// <summary>Gets the last published local storage commit position.</summary>
    long Position
    {
        get;
    }
    /// <summary>Runs a synchronous read while its consistent storage gate is held.</summary>
    /// <typeparam name="T">Owned result type.</typeparam>
    /// <param name="read">Action that must not retain the view or borrowed bytes.</param>
    /// <returns>The action's independently owned result.</returns>
    T Read<T>(Func<IKeyValueView, T> read);
    /// <summary>Runs synchronous discard-only preparation under the native store gate.</summary>
    /// <typeparam name="T">Independently owned result type.</typeparam>
    /// <param name="prepare">Callback receiving a scoped child transaction and actual next local position.</param>
    /// <returns>The owned result; no journal, tree, cache publication or durable position is changed.</returns>
    /// <remarks>Nested store-gate entry is invalid. Inside a transaction callback use that transaction's Prepare.</remarks>
    T Prepare<T>(Func<IAtomicTransaction, long, T> prepare);

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
    /// <param name="cancellationToken">Cancellation checked during native copying and verification.</param>
    /// <returns>The captured local storage position.</returns>
    long CreateBackup(string directory, CancellationToken cancellationToken = default);
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
