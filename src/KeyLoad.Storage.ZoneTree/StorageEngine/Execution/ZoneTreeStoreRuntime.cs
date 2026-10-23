using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using ZoneTree;
using static KeyLoad.Storage.ZoneTree.ZoneTreePersistenceFormat;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeStoreRuntime : IDisposable
{
    private bool disposed;
    private readonly Lock disposalGate = new();
    private bool closeAttempted;
    private ExceptionDispatchInfo? originalCloseFailure;
    private const string OriginalCloseUnjoined = "The original execution store close has not returned; retain its root.";
    internal StorageOwnerSettlement OwnershipSettlement
    {
        get;
    }
    internal void MarkDisposed()
    {
        disposed = true;
    }
    private long position;
    private readonly bool guarded;

    internal ZoneTreeStoreRuntime(ZoneTreeStoreOptions options, Guid? expectedNodeId = null, TimeProvider? timeProvider = null,
        StorageOwnerSettlement? ownershipSettlement = null)
    {
        _ = options.MaximumReadCutElapsed;
        Options = options;
        Clock = timeProvider ?? TimeProvider.System;
        guarded = expectedNodeId.HasValue;
        OwnershipSettlement = ownershipSettlement ?? new();
        OwnershipSettlement.BeginOpen();
        try
        {
            CacheLifecycle = new(this);
            View = new(this);
            Checkpoints = new(this);
            Backups = new(this);
            if (expectedNodeId is { } nodeId)
            {
                ZoneTreeStoreInitializer.OpenExisting(this, nodeId);
            }
            else
            {
                ZoneTreeStoreInitializer.Open(this);
                EmbeddedPointCache = ZoneTreeStorePointCacheOpening.Open(this);
            }
        }
        catch (Exception failure)
        {
            ZoneTreeStoreConstructionCleanup.Run(this, failure, guarded);
            throw;
        }
    }

    internal ZoneTreeStoreOptions Options
    {
        get;
    }
    internal TimeProvider Clock
    {
        get;
    }

    internal ReaderWriterLockSlim Gate { get; } = new();
    internal ZoneTreeReadCutLifecycle RuntimeReadCuts { get; } = new();
    internal FileStream Ownership { get; set; } = null!;
    internal FileStream Journal { get; set; } = null!;
    internal IZoneTree<Memory<byte>, Memory<byte>> Tree { get; set; } = null!;
    internal ZoneTreeMaintenanceLifetime? Maintenance
    {
        get; set;
    }
    internal IMaintainer Maintainer => Maintenance!.Maintainer;
    internal StoreIdentity Identity { get; set; } = null!;
    internal ZoneTreeReadCounters ReadCounters { get; } = new();
    internal ZoneTreePointCache? EmbeddedPointCache
    {
        get;
    }
    internal ZoneTreePointCacheLifecycle CacheLifecycle
    {
        get;
    }
    internal ZoneTreePointCache? PointCache => CacheLifecycle.MaintenanceCache;
    internal ZoneTreeReadView View
    {
        get;
    }
    internal ZoneTreeCheckpointManager Checkpoints
    {
        get;
    }
    internal ZoneTreeBackupRestore Backups
    {
        get;
    }
    internal bool Poisoned
    {
        get; set;
    }
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
            DatabasePhaseTelemetry.End(DatabasePhaseKind.TreeMutation, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        DatabasePhaseTelemetry.End(DatabasePhaseKind.TreeMutation, DatabasePhaseOutcome.Completed, started);
    }

    internal bool IsDisposed => disposed;

    public void Dispose()
    {
        RuntimeReadCuts.EnsureDisposalCanJoin();
        ZoneTreePointCacheStoreGate.RequireNotInOperation(this, CacheLifecycle);
        lock (disposalGate)
        {
            if (closeAttempted)
            {
                originalCloseFailure?.Throw();
                if (!OwnershipSettlement.IsReleased)
                {
                    throw new InvalidOperationException(OriginalCloseUnjoined);
                }
                return;
            }
            RuntimeReadCuts.EnsureDisposalCanJoin();
            if (!ZoneTreePointCacheStoreGate.CanBeginDisposal(this, CacheLifecycle))
            {
                throw new InvalidOperationException(OriginalCloseUnjoined);
            }
            closeAttempted = true;
            try
            {
                ZoneTreeStoreRuntimeDisposal.Run(this, guarded);
                OwnershipSettlement.ReleaseReturned();
            }
            catch (Exception failure)
            {
                originalCloseFailure = ExceptionDispatchInfo.Capture(failure);
                throw;
            }
        }
    }
}
