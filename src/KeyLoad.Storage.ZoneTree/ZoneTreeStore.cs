using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>
/// The checksummed redo journal is canonical. ZoneTree is its ordered materialization.
/// The gate prevents readers from seeing a partially applied transaction. No network work runs inside it.
/// </summary>
public sealed class ZoneTreeStore : IAtomicStore, IKeyValueView
{
    private const int NextJournalSequenceIncrement = 1;
    private const int NoMutations = 0;
    private const string InvalidNativeReadCutContextMessage = "A native read cut must be captured inside this store's gated read callback.";

    private readonly ZoneTreeStoreRuntime runtime;
    private readonly ZoneTreeStorageExecutionOptions? executionPolicy;
    private readonly ZoneTreePointCacheExecutionOptions? cacheExecutionPolicy;
    private const string CacheExecutionPolicyRequired = "A public native storage owner must configure point-cache execution policy before cache admission.";

    /// <summary>Gets the store's persisted identity with privately owned signing material.</summary>
    public StoreIdentity Identity => runtime.Identity;
    /// <summary>Gets the latest local durable journal position.</summary>
    public long Position => runtime.Position;

    /// <summary>Opens or creates a node-local store and replays its verified journal.</summary>
    /// <param name="options">Store directory, identity and persistence budgets.</param>
    /// <param name="executionOptions">Centrally validated storage execution policy, frozen before files are opened.</param>
    /// <param name="cacheExecutionOptions">Centrally validated cache policy, frozen before any optional memory admission.</param>
    /// <param name="timeProvider">Borrowed clock for native read-cut elapsed budgets; defaults to the system provider.</param>
    public ZoneTreeStore(ZoneTreeStoreOptions options, IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        IOptions<ZoneTreePointCacheExecutionOptions> cacheExecutionOptions, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Directory);
        options.EmbeddedPointCache?.Validate();
        ArgumentNullException.ThrowIfNull(executionOptions);
        executionPolicy = executionOptions.Value;
        ArgumentNullException.ThrowIfNull(executionPolicy);
        executionPolicy.Validate();
        ArgumentNullException.ThrowIfNull(cacheExecutionOptions);
        cacheExecutionPolicy = cacheExecutionOptions.Value;
        ArgumentNullException.ThrowIfNull(cacheExecutionPolicy);
        cacheExecutionPolicy.Validate();
        var resolved = options.WithExecutionSnapshot(executionPolicy);
        if (resolved.EmbeddedPointCache is { } embedded)
        {
            resolved = resolved with { EmbeddedPointCache = embedded.WithExecutionSnapshot(cacheExecutionPolicy) };
        }
        runtime = new(resolved, timeProvider: timeProvider);
    }

    internal ZoneTreeStore(ZoneTreeStoreRuntime runtime, Guid expectedNodeId)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (expectedNodeId == Guid.Empty || runtime.Identity.NodeId != expectedNodeId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, IdentityScopeInvalid);
        }
        this.runtime = runtime;
    }

    /// <summary>Runs a read callback while the consistent provider gate is held.</summary>
    /// <typeparam name="T">Owned result returned by the callback.</typeparam>
    /// <param name="read">Synchronous callback that cannot retain the view.</param>
    /// <returns>The callback's result after releasing the read gate.</returns>
    public T Read<T>(Func<IKeyValueView, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        ZoneTreePhaseGate.EnterRead(runtime);
        try
        {
            runtime.Check();
            return read(this);
        }
        finally
        {
            runtime.Gate.ExitReadLock();
        }
    }

    /// <summary>Compiles and durably publishes one atomic transaction.</summary>
    /// <typeparam name="T">Owned result returned by the transaction compiler.</typeparam>
    /// <param name="compile">Synchronous compiler receiving the staged transaction and proposed position.</param>
    /// <returns>The compiler result after the journal and materialized tree are published.</returns>
    public T Commit<T>(Func<IAtomicTransaction, long, T> compile)
    {
        ArgumentNullException.ThrowIfNull(compile);
        var holdStarted = ZoneTreePhaseGate.EnterCommit(runtime);
        var holdOutcome = DatabasePhaseOutcome.Faulted;
        try
        {
            runtime.Check();
            var tx = new ZoneTreeTransaction(runtime);
            var nextPosition = checked(runtime.Position + NextJournalSequenceIncrement);
            var result = compile(tx, nextPosition);
            var changes = tx.PrepareChanges();
            if (changes.Length == NoMutations)
            {
                holdOutcome = DatabasePhaseOutcome.Completed;
                return result;
            }

            ZoneTreeJournalPublication.Publish(runtime, tx, changes, nextPosition);
            holdOutcome = DatabasePhaseOutcome.Completed;
            return result;
        }
        finally
        {
            ZoneTreePhaseGate.ExitCommit(runtime, holdStarted, holdOutcome);
        }
    }

    internal ZoneTreeReadCutLease CaptureNativeReadCut(IKeyValueView view, ZoneTreeReadCutLimits limits,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(view, this) || !runtime.Gate.IsReadLockHeld || runtime.Gate.IsWriteLockHeld)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidNativeReadCutContextMessage);
        }

        return runtime.NativeReadCuts.Capture(runtime, limits, cancellationToken);
    }

    byte[]? IKeyValueView.ReadOwnedValue(byte[] key) => runtime.View.ReadOwnedValue(key);
    ScanPage IKeyValueView.Scan(byte[] prefix, int maxRecords, byte[]? afterKey) => runtime.View.Scan(prefix, maxRecords, afterKey);
    bool IKeyValueView.ReadValue(byte[] key, StorageValueReader reader, StorageReadObserver? observer)
        => runtime.View.ReadValue(key, reader, observer);
    StorageScanResult IKeyValueView.VisitRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, byte[]? untilKey, StorageReadObserver? observer, CancellationToken cancellationToken)
        => runtime.View.VisitRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);
    StorageScanResult IKeyValueView.VisitReverseRange(byte[] prefix, int maxRecords, StorageRecordVisitor visitor,
        byte[]? afterKey, byte[]? untilKey, StorageReadObserver? observer, CancellationToken cancellationToken)
        => runtime.View.VisitReverseRange(prefix, maxRecords, visitor, afterKey, untilKey, observer, cancellationToken);

    /// <summary>Writes a verified immutable snapshot at the current committed cut.</summary>
    /// <param name="path">New private file path for the snapshot.</param>
    /// <param name="expectedAppliedPosition">Optional replicated position that must match the captured cut.</param>
    /// <returns>Snapshot identity and committed positions.</returns>
    public StorageSnapshot CreateSnapshot(string path, long? expectedAppliedPosition = null)
        => runtime.Checkpoints.CreateSnapshot(path, expectedAppliedPosition);

    /// <summary>Compacts the canonical journal through a verified checkpoint.</summary>
    /// <returns>The compacted store cut.</returns>
    public StorageSnapshot Compact() => runtime.Checkpoints.Compact();

    /// <summary>Installs a verified snapshot at the required replicated position.</summary>
    /// <param name="path">Existing snapshot file to verify and install.</param>
    /// <param name="expectedAppliedPosition">Exact replicated position required by the installation.</param>
    /// <returns>The installed store cut.</returns>
    public StorageSnapshot InstallSnapshot(string path, long expectedAppliedPosition)
        => runtime.Checkpoints.InstallSnapshot(path, expectedAppliedPosition);

    /// <summary>Validates a snapshot without changing the live store.</summary>
    /// <param name="path">Existing snapshot file to verify.</param>
    /// <returns>The verified snapshot cut.</returns>
    public StorageSnapshot VerifySnapshot(string path) => runtime.Checkpoints.VerifySnapshot(path);

    /// <summary>Persists whether dispatch is paused for this store.</summary>
    /// <param name="paused">The requested persisted dispatch state.</param>
    public void SetDispatchPaused(bool paused)
    {
        runtime.Gate.EnterWriteLock();
        try
        {
            runtime.Check();
            runtime.Identity = runtime.Identity with { DispatchPaused = paused };
            ZoneTreeIdentityFile.Write(Path.Combine(runtime.Options.Directory, IdentityFileName), runtime.Identity, runtime.Options.IdentityBufferBytes);
        }
        finally
        {
            runtime.Gate.ExitWriteLock();
        }
    }

    /// <summary>Persists a monotonic minimum reader requirement under the exclusive physical owner.</summary>
    /// <param name="minimumReaderContract">The supported native runtime journal reader contract.</param>
    public void RequireReaderContract(int minimumReaderContract)
        => ZoneTreeReaderContractPublication.Require(runtime, minimumReaderContract);

    /// <summary>Copies the canonical journal and identity into a private verified backup directory.</summary>
    /// <param name="directory">Destination directory that must be empty.</param>
    /// <returns>The captured local journal position.</returns>
    public long CreateBackup(string directory) => runtime.Backups.CreateBackup(directory);

    /// <summary>Validates the original identity, checksums, native journal and committed cut of a backup.</summary>
    /// <param name="directory">The private immutable backup directory.</param>
    /// <param name="executionOptions">Validated native storage verification policy.</param>
    /// <returns>The original persisted identity and verified local journal position.</returns>
    public static (StoreIdentity Identity, long Position) VerifyBackup(string directory,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentNullException.ThrowIfNull(executionOptions);
        return ZoneTreeBackupVerification.Verify(directory, executionOptions);
    }

    /// <summary>Restores a verified backup under a new node identity with dispatch paused.</summary>
    /// <param name="backup">Directory containing the verified backup manifest and files.</param>
    /// <param name="destination">Empty private directory for the restored store.</param>
    /// <param name="executionOptions">Centrally validated policy frozen before restore begins.</param>
    /// <param name="newIncarnation">Optional replacement authority incarnation.</param>
    /// <param name="newSigningKey">Optional caller-owned signing key copied into the restored identity.</param>
    /// <returns>The restored persisted identity.</returns>
    public static StoreIdentity Restore(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, Guid? newIncarnation = null, byte[]? newSigningKey = null)
        => ZoneTreeBackupRestore.Restore(backup, destination, executionOptions, newIncarnation, newSigningKey);

    /// <summary>Returns cumulative logical read work for this store's nonpersisted diagnostics session.</summary>
    /// <remarks>
    /// Fields are observed independently; exact operation deltas require an isolated, quiescent window.
    /// These counters exclude physical I/O and remain readable after disposal.
    /// Saturated fields cannot establish further deltas. Reopen starts a new session.
    /// </remarks>
    /// <returns>An immutable snapshot without keys, payloads, credentials or storage paths.</returns>
    public ZoneTreeReadSnapshot GetReadDiagnostics() => runtime.ReadCounters.Snapshot();

    /// <summary>Returns disposable point-cache work and modeled ownership without data or secrets.</summary>
    /// <remarks>Current-helper counters reset on coordinated cold replacement; snapshots remain readable after disposal.</remarks>
    public ZoneTreePointCacheSnapshot GetPointCacheDiagnostics() => runtime.CacheLifecycle.Snapshot();

    /// <summary>Creates this opened runtime's sole, initially cold local point-cache control.</summary>
    /// <param name="options">Fixed limits and externally owned shared retention budget.</param>
    /// <param name="permit">Fixed receiver-owned eligibility capability; it grants no read authority.</param>
    /// <param name="control">The created control only for Created; null for every other result.</param>
    /// <returns>Created, AlreadyConfigured, Busy or Closed; invalid options and provider failures remain exceptions.</returns>
    public ZoneTreePointCacheControlResult TryCreateCoordinatedPointCache(ZoneTreePointCacheOptions options,
        ICacheReadPermit permit, out ZoneTreePointCacheControl? control)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(permit);
        options.Validate();
        var configured = cacheExecutionPolicy ?? throw new InvalidOperationException(CacheExecutionPolicyRequired);
        return runtime.CacheLifecycle.TryCreate(options.WithExecutionSnapshot(configured), permit, out control);
    }

    /// <summary>Disables acceleration and retires entries, preserving charges held by active readers.</summary>
    /// <remarks>Does not acquire the store gate, wait for callbacks or change durable data.</remarks>
    public void DisablePointCache() => runtime.CacheLifecycle.DisableAdmission();

    /// <summary>Closes the store handles and releases its exclusive directory lock.</summary>
    public void Dispose() => runtime.Dispose();
}
