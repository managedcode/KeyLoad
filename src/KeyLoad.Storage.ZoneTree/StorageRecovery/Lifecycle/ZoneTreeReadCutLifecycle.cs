using System.Runtime.ExceptionServices;
using ZoneTree;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutLifecycle
{
    private const string ClosedMessage = "The native read-cut runtime is closing.";
    private const string SlotBusyMessage = "The runtime already owns a native read-cut lease.";
    private const string TreeReplacementMessage = "The native tree cannot be replaced while a read-cut lease is active.";
    private readonly Lock sync = new();
    private ZoneTreeReadCutLease? active;
    private bool closed;

    internal ZoneTreeReadCutLease Capture(ZoneTreeStoreRuntime runtime, ZoneTreeReadCutLimits limits,
        CancellationToken cancellationToken)
    {
        ZoneTreeReadCutValidation.Validate(limits, runtime.Options);
        runtime.Check();
        ZoneTreeReadCutLease lease;
        lock (sync)
        {
            if (closed)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ClosedMessage);
            }
            if (active is not null)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, SlotBusyMessage);
            }

            lease = new ZoneTreeReadCutLease(limits, Release, runtime.Clock, cancellationToken);
            active = lease;
        }

        try
        {
            lease.CheckAfterCapture();
            var identity = runtime.Identity;
            var cut = new ZoneTreeNativeReadCut(identity.FormatVersion, identity.KeyCodecVersion, identity.NodeId,
                identity.Incarnation, identity.Durability, identity.DispatchPaused, identity.ReadGeneration, runtime.Position);
            var iterator = runtime.Tree.CreateIterator(IteratorType.Snapshot, includeDeletedRecords: false,
                contributeToTheBlockCache: false);
            lease.Attach(iterator, cut);
            lease.FinishCapture();
            lease.CheckAfterCapture();
            return lease;
        }
        catch (Exception failure)
        {
            lease.FinishCapture();
            try
            {
                lease.Dispose();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(failure, cleanupFailure);
            }
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw;
        }
    }

    internal void EnsureDisposalCanJoin()
    {
        ZoneTreeReadCutLease? lease;
        lock (sync)
        {
            lease = active;
        }
        lease?.EnsureDisposalCanJoin();
    }

    internal void RequireNoActiveLeaseForTreeReplacement()
    {
        lock (sync)
        {
            if (active is not null)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, TreeReplacementMessage);
            }
        }
    }

    internal void CloseAndJoin()
    {
        ZoneTreeReadCutLease? lease;
        lock (sync)
        {
            closed = true;
            lease = active;
        }
        lease?.Dispose();
    }

    private void Release(ZoneTreeReadCutLease lease)
    {
        lock (sync)
        {
            if (ReferenceEquals(active, lease))
            {
                active = null;
            }
        }
    }
}
