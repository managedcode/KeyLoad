using System.Runtime.InteropServices;
using ZoneTree;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeStoreRuntime : IDisposable
{
    private bool disposed;
    private int disposeStarted;
    private long position;

    internal ZoneTreeStoreRuntime(ZoneTreeStoreOptions options)
    {
        Options = options;
        try
        {
            View = new(this);
            Checkpoints = new(this);
            Backups = new(this);
            ZoneTreeStoreInitializer.Open(this);
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
        if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
        {
            return;
        }

        Gate.EnterWriteLock();
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ZoneTreeStoreHandleDisposal.Normal(this);
        }
        finally
        {
            Gate.ExitWriteLock();
            Gate.Dispose();
        }
    }
}
