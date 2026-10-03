using System.Runtime.InteropServices;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using ZoneTree;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeStoreRuntime : IDisposable
{
    private bool disposed;
    private long position;

    internal ZoneTreeStoreRuntime(ZoneTreeStoreOptions options)
    {
        Options = options;
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
    }

    internal ZoneTreeStoreOptions Options { get; }
    internal ReaderWriterLockSlim Gate { get; } = new();
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
        PointCache?.Invalidate(mutation.Key.Span);
        var key = MemoryMarshal.AsMemory(mutation.Key);
        if (mutation.Value is null)
        {
            Tree.ForceDelete(key);
            return;
        }

        var logicalValue = mutation.Value.Value;
        var value = new byte[logicalValue.Length + StorageValueHeaderBytes];
        value[0] = LiveValueMarker;
        logicalValue.Span.CopyTo(value.AsSpan(StorageValueHeaderBytes));
        Tree.Upsert(key, value);
    }

    public void Dispose()
    {
        if (!ZoneTreePointCacheStoreGate.CanBeginDisposal(this, CacheLifecycle)
            || !CacheLifecycle.TryBeginClose(out var control))
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
