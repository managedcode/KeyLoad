using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using KeyLoad.Diagnostics.ResourceExecution;
using ZoneTree;
using ZoneTree.Transactional;
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
        Volatile.Write(ref disposed, true);
    }
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
            View = new(this);
            if (expectedNodeId is { } nodeId)
            {
                ZoneTreeStoreInitializer.OpenExisting(this, nodeId);
            }
            else
            {
                ZoneTreeStoreInitializer.Open(this);
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

    internal AsyncLocal<ZoneTreeOperationContext?> Operation { get; } = new();
    internal ZoneTreeOperationContext CurrentOperation
    {
        get
        {
            var current = Operation.Value ?? throw new InvalidOperationException(ZoneTreeStorePreparationExecution.OutsideTransactionCallback);
            current.Check();
            return current;
        }
    }
    internal ZoneTreeReadCutLifecycle RuntimeReadCuts { get; } = new();
    internal FileStream Ownership { get; set; } = null!;
    internal ITransactionalZoneTree<Memory<byte>, Memory<byte>> Tree { get; set; } = null!;
    internal IZoneTree<Memory<byte>, global::ZoneTree.PresetTypes.Deletable<Memory<byte>>> PlainTree { get; set; } = null!;
    internal IMaintainer? PlainMaintainer { get; set; }
    internal IMaintainer? Maintainer
    {
        get; set;
    }
    internal StoreIdentity Identity { get; set; } = null!;
    internal ZoneTreeReadCounters ReadCounters { get; } = new();
    internal ZoneTreeReadView View
    {
        get;
    }
    private volatile bool poisoned;
    internal bool Poisoned
    {
        get => poisoned;
        set => poisoned = value;
    }
    internal long Position => Operation.Value?.Position ?? ZoneTreeStorePosition.Read(Tree);

    internal void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, typeof(ZoneTreeStore));
        if (Poisoned)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, RecoveryRequired);
        }
    }

    internal static void Apply(ITransaction<Memory<byte>, Memory<byte>> transaction, StorageMutation mutation)
    {
        var started = DatabasePhaseTelemetry.Begin();
        try
        {
            var key = MemoryMarshal.AsMemory(mutation.Key);
            if (mutation.Value is null)
            {
                transaction.Delete(key);
            }
            else
            {
                var value = MemoryMarshal.AsMemory(mutation.Value.Value);
                transaction.Upsert(key, value);
            }
        }
        catch (Exception)
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.TreeMutation, DatabasePhaseOutcome.Faulted, started);
            throw;
        }

        DatabasePhaseTelemetry.End(DatabasePhaseKind.TreeMutation, DatabasePhaseOutcome.Completed, started);
    }

    internal bool IsDisposed => Volatile.Read(ref disposed);

    public void Dispose()
    {
        RuntimeReadCuts.EnsureDisposalCanJoin();
        if (Operation.Value is not null)
        {
            throw new InvalidOperationException(OriginalCloseUnjoined);
        }
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
