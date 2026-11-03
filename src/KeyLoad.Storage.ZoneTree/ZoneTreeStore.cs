using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>
/// ZoneTree owns transactional snapshots, plain-table records and both write-ahead logs.
/// Transactional reads and the store position share one native MVCC snapshot.
/// </summary>
public sealed class ZoneTreeStore : IAtomicStore, IKeyValueView, ICooperativeRangeReadView
{
    private const string InvalidReadCutContextMessage = "An execution read cut must be captured inside this store's native read callback.";

    private readonly ZoneTreeStoreRuntime runtime;

    /// <summary>Gets the store's persisted identity with privately owned signing material.</summary>
    public StoreIdentity Identity => runtime.Identity;
    /// <summary>Gets the latest published local commit position.</summary>
    public long Position => runtime.Position;

    /// <summary>Validates current identity admission without creating or recovering a store.</summary>
    /// <param name="options">The same directory and identity requirements used by the future owner.</param>
    /// <param name="executionOptions">The centrally validated storage execution policy.</param>
    /// <remarks>The caller retains node ownership; actual store open repeats all execution checks.</remarks>
    public static void ValidateIdentityBeforeOpen(ZoneTreeStoreOptions options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
        => ZoneTreeIdentityPreflight.Validate(options, executionOptions);

    /// <summary>Opens or creates a node-local store; ZoneTree recovers it from its own write-ahead log.</summary>
    /// <param name="options">Store directory, identity and persistence budgets.</param>
    /// <param name="executionOptions">Centrally validated storage execution policy, frozen before files are opened.</param>
    /// <param name="timeProvider">Borrowed clock for execution read-cut elapsed budgets; defaults to the system provider.</param>
    public ZoneTreeStore(ZoneTreeStoreOptions options, IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        TimeProvider? timeProvider = null)
        : this(options, executionOptions, timeProvider, new StorageOwnerSettlement())
    {
    }

    internal static ZoneTreeStore OpenOwned(ZoneTreeStoreOptions options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        StorageOwnerSettlement settlement, TimeProvider? timeProvider = null)
        => new(options, executionOptions, timeProvider, settlement);

    private ZoneTreeStore(ZoneTreeStoreOptions options, IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        TimeProvider? timeProvider, StorageOwnerSettlement settlement)
    {
        var frozen = ZoneTreeStoreOpeningOptions.Freeze(options, executionOptions);
        runtime = new(frozen, timeProvider: timeProvider, ownershipSettlement: settlement);
    }

    internal ZoneTreeStore(ZoneTreeStoreRuntime runtime, Guid expectedNodeId)
    {
        ArgumentNullException.ThrowIfNull(runtime) /* KLD0041: BCL null validation accepts the runtime ZoneTreeStoreRuntime reference through its object parameter. */;
        if (expectedNodeId == Guid.Empty || runtime.Identity.NodeId != expectedNodeId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, IdentityScopeInvalid);
        }
        this.runtime = runtime;
    }

    /// <inheritdoc />
    public int MaximumRangeRecords => runtime.Options.MaximumRangeRecords;

    /// <summary>Runs a callback on a native MVCC snapshot; plain-table reads have their native non-transactional semantics.</summary>
    /// <typeparam name="T">Owned result returned by the callback.</typeparam>
    /// <param name="read">Synchronous callback that cannot retain the view.</param>
    /// <returns>The callback's result after releasing the native read transaction.</returns>
    public T Read<T>(Func<IKeyValueView, T> read)
    {
        ArgumentNullException.ThrowIfNull(read) /* KLD0041: BCL null validation accepts the read Func reference through its object parameter. */;
        using var operation = new ZoneTreeOperationContext(runtime, runtime.Tree.BeginReadOnlyTransaction(), ownsTransaction: true);
        return read(this);
    }

    /// <inheritdoc />
    public async ValueTask<T> ReadAsync<T>(Func<IKeyValueView, CancellationToken, ValueTask<T>> read,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read) /* KLD0041: The BCL null guard accepts this existing typed callback reference without copying its contents. */;
        cancellationToken.ThrowIfCancellationRequested();
        using var operation = new ZoneTreeOperationContext(runtime, runtime.Tree.BeginReadOnlyTransaction(), ownsTransaction: true);
        return await read(this, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public T Prepare<T>(Func<IAtomicTransaction, long, T> prepare)
        => ZoneTreeStorePreparationExecution.Prepare(runtime, prepare);

    /// <summary>Compiles one atomic ZoneTree transaction and publishes it into the native process-persistent WAL.</summary>
    /// <typeparam name="T">Owned result returned by the transaction compiler.</typeparam>
    /// <param name="compile">Synchronous compiler receiving the staged transaction and proposed position.</param>
    /// <returns>The compiler result after the ZoneTree transaction is committed.</returns>
    public T Commit<T>(Func<IAtomicTransaction, long, T> compile)
        => ZoneTreeStoreCommitExecution.Commit(runtime, compile);

    /// <inheritdoc />
    public Task<T> CommitAsync<T>(Func<IAtomicTransaction, long, T> compile, CancellationToken cancellationToken = default)
        => ZoneTreeStoreCommitExecution.CommitAsync(runtime, compile, cancellationToken);

    /// <inheritdoc />
    public Task SyncAsync(CancellationToken cancellationToken = default)
        => ZoneTreeStoreCommitExecution.SyncAsync(runtime, cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateBackupAsync(string directory, CancellationToken cancellationToken = default)
        => ZoneTreeBackup.CreateAsync(runtime, directory, cancellationToken);

    /// <summary>Restores the latest ZoneTree backup generation under a new node identity with dispatch paused.</summary>
    /// <param name="backup">Directory written by <see cref="CreateBackupAsync"/>.</param>
    /// <param name="destination">Empty directory for the restored store.</param>
    /// <param name="executionOptions">Validated storage execution policy.</param>
    /// <param name="newIncarnation">Optional replacement authority incarnation.</param>
    /// <param name="newSigningKey">Optional signing key copied into the restored identity.</param>
    /// <param name="cancellationToken">Cancellation checked before the destination is created.</param>
    /// <returns>The restored persisted identity.</returns>
    public static Task<StoreIdentity> RestoreAsync(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, Guid? newIncarnation = null,
        byte[]? newSigningKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backup);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(executionOptions) /* KLD0041: BCL null validation accepts the executionOptions IOptions reference through its object parameter. */;
        return ZoneTreeBackup.RestoreAsync(backup, destination, executionOptions, newIncarnation, newSigningKey,
            cancellationToken);
    }

    /// <summary>Bounds caller work for one captured read cut by the configured storage read-cut ceiling.</summary>
    internal ZoneTreeReadCutLimits BoundReadCut(int records, long bytes, TimeSpan remaining)
    {
        var options = runtime.Options;
        return new(Math.Min(records, options.MaximumReadCutRecords), Math.Min(bytes, options.MaximumReadCutExaminedBytes),
            remaining == Timeout.InfiniteTimeSpan || remaining > options.MaximumReadCutElapsed
                ? options.MaximumReadCutElapsed : remaining);
    }

    internal ZoneTreeReadCutLease CaptureReadCut(IKeyValueView view, ZoneTreeReadCutLimits limits,
        CancellationToken cancellationToken)
    {
        if (!(ReferenceEquals(view, this) /* KLD0041: Require this store instance to own the active read cut through CLR reference identity. */) || runtime.Operation.Value is not { Writable: false })
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReadCutContextMessage);
        }

        return runtime.RuntimeReadCuts.Capture(runtime, limits, cancellationToken);
    }

    byte[]? IKeyValueView.ReadOwnedValue(byte[] key) => runtime.View.ReadOwnedValue(key);
    ScanPage IKeyValueView.Scan(byte[] prefix, int maxRecords, byte[]? afterKey) => runtime.View.Scan(prefix, maxRecords, afterKey);
    bool IKeyValueView.ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer)
        => runtime.View.ReadValue(key, reader, observer);

    ValueTask<StorageScanResult> ICooperativeRangeReadView.VisitRangeAsync(byte[] prefix, int maxRecords,
        StorageRecordVisitor visitor, byte[]? afterKey, CancellationToken cancellationToken)
        => ZoneTreeRangeReader.VisitAsync(runtime, prefix, maxRecords, visitor, afterKey, cancellationToken);
    IOrderedPointReader ICooperativeRangeReadView.OpenOrderedPointReader(byte[] prefix, CancellationToken cancellationToken)
        => new ZoneTreeOrderedPointReader(runtime, prefix, cancellationToken);
    StorageScanResult IKeyValueView.VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, byte[]? untilKey, StorageReadObserver? observer, CancellationToken cancellationToken)
        => runtime.View.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
    StorageScanResult IKeyValueView.VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, byte[]? untilKey, StorageReadObserver? observer, CancellationToken cancellationToken)
        => runtime.View.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);

    /// <summary>Returns cumulative logical read work for this store's nonpersisted diagnostics session.</summary>
    /// <remarks>
    /// Fields are observed independently; exact operation deltas require an isolated, quiescent window.
    /// These counters exclude physical I/O and remain readable after disposal.
    /// Saturated fields cannot establish further deltas. Reopen starts a new session.
    /// </remarks>
    /// <returns>An immutable snapshot without keys, payloads, credentials or storage paths.</returns>
    public ZoneTreeReadSnapshot GetReadDiagnostics() => runtime.ReadCounters.Snapshot();

    internal global::ZoneTree.IMaintainer Maintainer => runtime.Maintainer!;

    /// <summary>Closes the store handles and releases its exclusive directory lock.</summary>
    public void Dispose() => runtime.Dispose();
}
