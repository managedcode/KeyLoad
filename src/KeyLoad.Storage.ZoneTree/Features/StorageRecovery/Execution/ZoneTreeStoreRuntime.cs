using System.Runtime.InteropServices;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using ZoneTree;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeStoreRuntime : IDisposable
{
    private bool disposed;
    private long position;
    private readonly bool guarded;

    internal ZoneTreeStoreRuntime(ZoneTreeStoreOptions options, Guid? expectedNodeId = null)
    {
        Options = options;
        guarded = expectedNodeId.HasValue;
        if (expectedNodeId is not { } nodeId)
        {
            CacheLifecycle = new(this);
            try
            {
                View = new(this);
                Checkpoints = new(this);
                Backups = new(this);
                ZoneTreeStoreInitializer.Open(this);
                EmbeddedPointCache = OpenPointCache();
            }
            catch (Exception)
            {
                Gate.Dispose();
                throw;
            }
            return;
        }
        try
        {
            CacheLifecycle = new(this);
            View = new(this);
            Checkpoints = new(this);
            Backups = new(this);
            ZoneTreeStoreInitializer.OpenExisting(this, nodeId);
        }
        catch (Exception failure)
        {
            ZoneTreeExistingStoreCleanup.FailedConstruction(this, failure);
            throw;
        }
    }

    internal ZoneTreeStoreOptions Options { get; }
    internal ReaderWriterLockSlim Gate { get; } = new();
    internal ZoneTreeReadCutLifecycle NativeReadCuts { get; } = new();
    internal FileStream Ownership { get; set; } = null!;
    internal FileStream Journal { get; set; } = null!;
    internal IZoneTree<Memory<byte>, Memory<byte>> Tree { get; set; } = null!;
    internal IMaintainer Maintainer { get; set; } = null!;
    internal StoreIdentity Identity { get; set; } = null!;
    internal ZoneTreeReadCounters ReadCounters { get; } = new();
    internal ZoneTreePointCache? EmbeddedPointCache { get; }
    internal ZoneTreePointCacheLifecycle CacheLifecycle { get; }
    internal ZoneTreePointCache? PointCache => CacheLifecycle.MaintenanceCache;
    internal ZoneTreeReadView View { get; }
    internal ZoneTreeCheckpointManager Checkpoints { get; }
    internal ZoneTreeBackupRestore Backups { get; }
    internal bool Poisoned { get; set; }
    internal long Position => Interlocked.Read(ref position);
    internal void SetPosition(long value) => position = value;

    internal void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, typeof(ZoneTreeStore));
        if (Poisoned)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RecoveryRequired);
        }
    }

    internal void Apply(StorageMutation mutation)
    {
        var started = DatabasePhaseTelemetry.Begin();
        try
        {
            PointCache?.Invalidate(mutation.Key.Span);
            var key = MemoryMarshal.AsMemory(mutation.Key);
            if (mutation.Value is null)
            {
                Tree.ForceDelete(key);
            }
            else
            {
                var logicalValue = mutation.Value.Value;
                var value = new byte[logicalValue.Length + StorageValueHeaderBytes];
                value[0] = LiveValueMarker;
                logicalValue.Span.CopyTo(value.AsSpan(StorageValueHeaderBytes));
                Tree.Upsert(key, value);
            }
        }
        catch (Exception)
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        DatabasePhaseTelemetry.End(DatabasePhaseKind.NativeTreeMutation, DatabasePhaseOutcome.Completed, started);
    }

    public void Dispose()
    {
        NativeReadCuts.EnsureDisposalCanJoin();
        if (!ZoneTreePointCacheStoreGate.CanBeginDisposal(this, CacheLifecycle))
        {
            return;
        }
        NativeReadCuts.CloseAndJoin();
        if (guarded)
        {
            DisposeExisting();
            return;
        }
        if (!CacheLifecycle.TryBeginClose(out var control))
        {
            return;
        }

        var failures = new List<Exception>();
        ZoneTreePointCacheCleanup.Capture(() =>
        {
            if (control is not null)
            {
                control.CloseAdmission();
            }
            else
            {
                EmbeddedPointCache?.Disable();
            }
        }, failures);
        Gate.EnterWriteLock();
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ZoneTreePointCacheCleanup.Capture(() => ZoneTreeStoreHandleDisposal.Normal(this), failures);
            ZoneTreePointCacheCleanup.Capture(CacheLifecycle.DisposeCacheUnderWrite, failures);
        }
        finally
        {
            ZoneTreePointCacheCleanup.Capture(Gate.ExitWriteLock, failures);
            ZoneTreePointCacheCleanup.Capture(Gate.Dispose, failures);
        }
        ZoneTreePointCacheCleanup.ThrowFailures(failures);
    }

    private void DisposeExisting()
    {
        if (!CacheLifecycle.TryBeginClose(out var control))
        {
            return;
        }
        var failures = new List<Exception>();
        ZoneTreeExistingStoreCleanup.Capture(() => control?.CloseAdmission(), failures);
        var entered = false;
        ZoneTreeExistingStoreCleanup.Capture(() =>
        {
            Gate.EnterWriteLock();
            entered = true;
            disposed = true;
        }, failures);
        ZoneTreeExistingStoreCleanup.CloseRegistered(this, failures);
        ZoneTreeExistingStoreCleanup.Capture(CacheLifecycle.DisposeCacheUnderWrite, failures);
        if (entered)
        {
            ZoneTreeExistingStoreCleanup.Capture(Gate.ExitWriteLock, failures);
        }
        ZoneTreeExistingStoreCleanup.Capture(Gate.Dispose, failures);
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
    }

    private ZoneTreePointCache? OpenPointCache()
    {
        if (Options.EmbeddedPointCache is not { } options)
        {
            return null;
        }

        try
        {
            return new(options);
        }
        catch (Exception)
        {
            ZoneTreeStoreHandleDisposal.FailedOpen(this);
            throw;
        }
    }
}
