using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Storage.ZoneTree;

internal sealed class ZoneTreeReadCutLifecycle
{
    private const string ClosedMessage = "The execution read-cut runtime is closing.";
    private readonly ConcurrentDictionary<ZoneTreeReadCutLease, byte> active = new();
    private int closed;

    internal ZoneTreeReadCutLease Capture(ZoneTreeStoreRuntime runtime, ZoneTreeReadCutLimits limits,
        CancellationToken cancellationToken)
    {
        ZoneTreeReadCutValidation.Validate(limits, runtime.Options);
        runtime.Check();
        var lease = new ZoneTreeReadCutLease(limits, Release, runtime.Clock, cancellationToken);
        active.TryAdd(lease, 0);
        try
        {
            // Registration precedes the close check: closing either joins this capture or it never touches a tree.
            if (Volatile.Read(ref closed) != 0)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ClosedMessage);
            }
            lease.CheckAfterCapture();
            var identity = runtime.Identity;
            var cut = new ZoneTreeReadCut(identity.NodeId,
                identity.Incarnation, identity.Durability, identity.DispatchPaused, identity.ReadGeneration, runtime.Position);
            var snapshot = new ZoneTreeSnapshotCursor(runtime, capturePlain: true);
            lease.Attach(snapshot, cut);
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
        foreach (var lease in active.Keys)
        {
            lease.EnsureDisposalCanJoin();
        }
    }

    internal void CloseAndJoin()
    {
        Interlocked.Exchange(ref closed, 1);
        var failures = new List<Exception>();
        foreach (var lease in active.Keys)
        {
            ZoneTreeExistingStoreCleanup.Capture(lease.Dispose, failures);
        }
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
    }

    private void Release(ZoneTreeReadCutLease lease)
    {
        active.TryRemove(lease, out _);
    }
}
