namespace KeyLoad.Storage;

/// <summary>Owns native read snapshots, record publication and write-ahead log durability.</summary>
/// <remarks>
/// Inside a read callback, Position and the scalar Identity fields describe the same protected cut as the view.
/// Effective record publications advance Position. A returned publication survives process termination; acknowledgements
/// require no additional disk sync. Explicit <see cref="SyncAsync"/> requests a stronger disk barrier. Uncertain or failed
/// live publication or explicit sync rejects subsequent reads until recovery.
/// Pure validation or compile rejection before publication preserves the unchanged healthy cut.
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
    /// <summary>Runs a synchronous callback on one native transactional snapshot.</summary>
    /// <typeparam name="T">Owned result type.</typeparam>
    /// <param name="read">Action that must not retain the view or borrowed bytes.</param>
    /// <returns>The action's independently owned result.</returns>
    T Read<T>(Func<IKeyValueView, T> read);
    /// <summary>Awaits a callback on one native snapshot, including awaited result delivery.</summary>
    /// <typeparam name="T">Owned result type.</typeparam>
    /// <param name="read">Callback whose view and borrowed bytes cannot escape its completion.</param>
    /// <param name="cancellationToken">Original caller cancellation; the callback is joined before its snapshot closes.</param>
    /// <returns>The callback result after native snapshot release.</returns>
    ValueTask<T> ReadAsync<T>(Func<IKeyValueView, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken = default);
    /// <summary>Runs synchronous discard-only preparation on a native read snapshot.</summary>
    /// <typeparam name="T">Independently owned result type.</typeparam>
    /// <param name="prepare">Callback receiving a scoped child transaction and actual next local position.</param>
    /// <returns>The owned result; no journal, tree, cache publication or durable position is changed.</returns>
    /// <remarks>Nested store-operation entry is invalid. Inside a transaction callback use that transaction's Prepare.</remarks>
    T Prepare<T>(Func<IAtomicTransaction, long, T> prepare);

    /// <summary>Compiles and publishes one atomic set of staged writes.</summary>
    /// <typeparam name="T">Owned committed result type.</typeparam>
    /// <param name="compile">Action receiving the staged transaction and proposed local position.</param>
    /// <returns>The result after successful commit publication.</returns>
    T Commit<T>(Func<IAtomicTransaction, long, T> compile);
    /// <summary>Publishes compiled writes, ordering process-persistent plain data before transactional command outcomes.</summary>
    /// <typeparam name="T">Owned result type.</typeparam>
    /// <param name="compile">Synchronous compiler; table data may be non-transactional.</param>
    /// <param name="cancellationToken">Cancellation before physical publication starts.</param>
    /// <returns>The result after process-persistent publication of its data and command outcome.</returns>
    Task<T> CommitAsync<T>(Func<IAtomicTransaction, long, T> compile, CancellationToken cancellationToken = default);
    /// <summary>Explicitly syncs every preceding publication to the provider's disk boundary.</summary>
    /// <param name="cancellationToken">Cancellation of the wait; the publications themselves stay published.</param>
    /// <returns>Completion of the native disk sync; device and power-loss qualification remain separate.</returns>
    Task SyncAsync(CancellationToken cancellationToken = default);
    /// <summary>Writes one provider backup generation of a consistent cut into an empty directory.</summary>
    /// <param name="directory">Destination directory owned by the backup caller.</param>
    /// <param name="cancellationToken">Cancellation of the backup generation.</param>
    /// <returns>A position through which the backup holds every commit.</returns>
    Task<long> CreateBackupAsync(string directory, CancellationToken cancellationToken = default);
}
