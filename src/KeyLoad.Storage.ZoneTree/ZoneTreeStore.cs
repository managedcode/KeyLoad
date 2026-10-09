using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>
/// The checksummed redo journal is canonical. ZoneTree is its ordered materialization.
/// The gate prevents readers from seeing a partially applied transaction. No network work runs inside it.
/// </summary>
public sealed class ZoneTreeStore : IAtomicStore, IKeyValueView, INativeCatalogBackupStore
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

    /// <summary>Native UTF8 size of the fixed encoded-frame rejection detail, for bounded transport reservation.</summary>
    public static int EncodedFrameLimitRejectionDetailBytes => ZoneTreeEncodedFrameRejection.DetailBytes;

    /// <summary>Classifies only the owning native encoded-frame rejection; it grants no execution authority.</summary>
    /// <param name="result">The actual resolved operation outcome, or no outcome.</param>
    /// <returns>Whether the exact stored error is the native encoded frame limit refusal.</returns>
    public static bool IsEncodedFrameLimitRejection(OperationResult? result)
        => ZoneTreeEncodedFrameRejection.Matches(result);

    /// <summary>Validates current identity admission without creating or recovering a store.</summary>
    /// <param name="options">The same directory and identity requirements used by the future owner.</param>
    /// <param name="executionOptions">The centrally validated storage execution policy.</param>
    /// <remarks>The caller retains node ownership; actual store open repeats all native checks.</remarks>
    public static void ValidateIdentityBeforeOpen(ZoneTreeStoreOptions options,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions)
        => ZoneTreeIdentityPreflight.Validate(options, executionOptions);

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
        var resolved = options.ResolveExecutionOptions(executionOptions);
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

    /// <summary>Copies the canonical journal and identity into a private verified backup directory.</summary>
    /// <param name="directory">Destination directory that must be empty.</param>
    /// <returns>The captured local journal position.</returns>
    public long CreateBackup(string directory) => runtime.Backups.CreateBackup(directory);

    /// <inheritdoc />
    public NativeCatalogBackupCapture CreateCatalogBackup(string directory,
        Func<IKeyValueView, long, StoreIdentity, byte[]> capture, CancellationToken cancellationToken)
        => runtime.Backups.CreateCatalogBackup(directory, capture, cancellationToken);

    /// <inheritdoc />
    public NativeCatalogBackupCapture CaptureOrReadCatalogBackup(string directory, Action<IKeyValueView> authorize,
        Func<IKeyValueView, long, StoreIdentity, byte[]> capture, Action<NativeCatalogBackupCapture> requireCaptured,
        CancellationToken cancellationToken)
        => runtime.Backups.CaptureOrReadCatalogBackup(directory, authorize, capture, requireCaptured, cancellationToken);

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

    /// <summary>Verifies the complete native owner archive and reads its exact bounded catalog cut metadata.</summary>
    /// <param name="backup">Published native catalog archive directory.</param>
    /// <param name="executionOptions">Original centrally validated native storage execution policy.</param>
    /// <param name="cancellationToken">Original owning operation cancellation.</param>
    /// <returns>The verified original physical position and exact owned cut metadata.</returns>
    public static NativeCatalogBackupCapture ReadVerifiedCatalogBackup(string backup,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions, CancellationToken cancellationToken) =>
        ZoneTreeCatalogBackupMetadataFile.ReadArchive(backup, executionOptions, cancellationToken);

    /// <summary>Runs a bounded offline reader against actual recovered original archive state under the native gate.</summary>
    /// <typeparam name="T">Independent owned result; the callback cannot retain its gated view.</typeparam>
    /// <param name="directory">Original immutable validated archive directory.</param>
    /// <param name="executionOptions">Original centrally validated native policy owner.</param>
    /// <param name="read">Borrowed current original-cut native view, never snapshot-supplied authority.</param>
    /// <param name="cancellationToken">Original operation cancellation.</param>
    /// <returns>The genuine callback result after all temporary native ownership is joined.</returns>
    public static T ReadVerifiedCatalogBackup<T>(string directory,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        Func<IKeyValueView, StoreIdentity, long, ReadOnlyMemory<byte>, T> read, CancellationToken cancellationToken)
        => ZoneTreeCatalogArchiveView.Read(directory, executionOptions, read, cancellationToken);

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

    /// <summary>Restores a verified catalog archive only after original native cut validation in unpublished staging.</summary>
    /// <param name="backup">Complete immutable native catalog owner archive.</param>
    /// <param name="destination">Clean operator-owned unpublished target canonical directory.</param>
    /// <param name="executionOptions">Original centrally validated native storage execution policy.</param>
    /// <param name="verifyCatalog">Synchronous actual credential and complete cut validator; it cannot retain or re-enter the view.</param>
    /// <param name="newIncarnation">Explicit new RF3 group incarnation.</param>
    /// <param name="newSigningKey">Actual new group signing key, retained only in private operator configuration.</param>
    /// <param name="cancellationToken">Original owning offline workflow cancellation.</param>
    /// <returns>The genuinely restored native identity with dispatch paused.</returns>
    public static StoreIdentity RestoreCatalogBackup(string backup, string destination,
        IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        Action<IAtomicTransaction, StoreIdentity, long, ReadOnlyMemory<byte>> verifyCatalog,
        Guid newIncarnation, byte[] newSigningKey, CancellationToken cancellationToken) =>
        ZoneTreeCatalogRestoreEntry.Restore(backup, destination, executionOptions, verifyCatalog,
            newIncarnation, newSigningKey, cancellationToken);

    /// <summary>Returns cumulative logical read work for this store's nonpersisted diagnostics session.</summary>
    /// <remarks>
    /// Fields are observed independently; exact operation deltas require an isolated, quiescent window.
    /// These counters exclude physical I/O and remain readable after disposal.
    /// Saturated fields cannot establish further deltas. Reopen starts a new session.
    /// </remarks>
    /// <returns>An immutable snapshot without keys, payloads, credentials or storage paths.</returns>
    public ZoneTreeReadSnapshot GetReadDiagnostics() => runtime.ReadCounters.Snapshot();

    /// <summary>Samples native write-gate contention without acquiring that gate.</summary>
    /// <remarks>Sample only while the owner is open. Counts include maintenance, are transient and confer no authority.</remarks>
    /// <returns>Only the ephemeral store session and native pending writer count; no data or secrets.</returns>
    public ZoneTreeGateSnapshot GateDiagnostics
        => new(runtime.ReadCounters.SessionId, runtime.Gate.WaitingWriteCount);

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

    /// <summary>Continues the same admitted native catalog restore slot and returns only actual joined transaction evidence.</summary>
    /// <param name="backup">Exact original verified immutable four-file native archive.</param>
    /// <param name="destination">Initially absent owned slot publication path.</param>
    /// <param name="stage">Original deterministic admitted unpublished stage.</param>
    /// <param name="executionOptions">Original centrally validated native policy owner.</param>
    /// <param name="clock">The original configured owning clock.</param>
    /// <param name="context">Immutable original operation/source/target identity.</param>
    /// <param name="targetSigningKey">Separately supplied original target signer, never persisted in CLI plan.</param>
    /// <param name="verifyOrReconcile">Actual original-source transaction callback; its long value is the native next commit position.</param>
    /// <param name="verifyRecovered">Actual complete recovered-state and fresh persisted operator verification.</param>
    /// <param name="observer">Optional internal post-barrier process-cut observation; supplies no authority.</param>
    /// <param name="cancellationToken">Original downward cancellation.</param>
    /// <returns>Genuine native rows independently observed after stores/readers have joined.</returns>
    public static NativeCatalogRestoreSlotCompletion RestoreCatalogBackupSlot(string backup, string destination,
        string stage, Microsoft.Extensions.Options.IOptions<ZoneTreeStorageExecutionOptions> executionOptions,
        TimeProvider clock, ClusterRestoreSlotContext context, ReadOnlyMemory<byte> targetSigningKey,
        Action<IAtomicTransaction, StoreIdentity, long, ReadOnlyMemory<byte>, ClusterRestoreSlotContext> verifyOrReconcile,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verifyRecovered,
        Action<NativeClusterRestoreStage>? observer, CancellationToken cancellationToken)
        => ZoneTreeRestoreSlotDriver.Restore(backup, destination, stage, executionOptions, clock, context,
            targetSigningKey, verifyOrReconcile, verifyRecovered, observer, cancellationToken);

    /// <summary>Requires the exact existing original completed slot without copy, reconciliation or authority reset.</summary>
    /// <param name="backup">The unchanged verified native source archive.</param>
    /// <param name="destination">The originally admitted existing slot.</param>
    /// <param name="executionOptions">The original centrally validated storage options.</param>
    /// <param name="clock">The original owning clock.</param>
    /// <param name="context">The immutable admitted slot identity and source cut.</param>
    /// <param name="targetSigningKey">The separately supplied original signer.</param>
    /// <param name="verifyRecovered">The complete current native state and persisted operator verification.</param>
    /// <param name="cancellationToken">Original downward cancellation.</param>
    /// <returns>The genuine original slot completion after the native owner is joined.</returns>
    public static NativeCatalogRestoreSlotCompletion ObserveCatalogBackupSlot(string backup, string destination,
        Microsoft.Extensions.Options.IOptions<ZoneTreeStorageExecutionOptions> executionOptions, TimeProvider clock,
        ClusterRestoreSlotContext context, ReadOnlyMemory<byte> targetSigningKey,
        Action<IKeyValueView, StoreIdentity, long, ClusterRestoreSlotContext> verifyRecovered,
        CancellationToken cancellationToken)
        => ZoneTreeRestoreSlotObserver.Require(backup, destination, executionOptions, clock, context,
            targetSigningKey, verifyRecovered, cancellationToken);

    /// <summary>Closes the store handles and releases its exclusive directory lock.</summary>
    public void Dispose() => runtime.Dispose();
}
