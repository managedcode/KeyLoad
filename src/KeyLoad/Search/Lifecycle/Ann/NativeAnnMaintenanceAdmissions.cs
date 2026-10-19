using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnMaintenanceAdmissions
{
    private const int Empty = 0;
    private readonly Lock gate = new();
    private readonly TaskCompletionSource settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closed;
    private int active;
    private (Guid Id, string Principal, AnnMaintenanceRequest Request, NativeMaintenanceSessionWork Work)? retainedWork;

    internal NativeMaintenanceSessionWork RequireWork(string principal, AnnMaintenanceCapabilityRequest request,
        ServerRuntimeOptions configured, TimeProvider clock, DateTimeOffset? expiry, CancellationToken shutdown)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (retainedWork is { } original)
            {
                if (original.Id != request.SessionId || original.Principal != principal
                    || !NativeAnnOriginalResults.SameRequest(original.Request, request.Maintenance))
                {
                    throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
                }
                original.Work.RequireExpiry(expiry);
                return original.Work;
            }
            if (request.Kind is not (AnnMaintenanceCapabilityKind.AdmitOriginal or AnnMaintenanceCapabilityKind.Begin))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, NativeAnnProtocol.Ownership);
            }
            var work = new NativeMaintenanceSessionWork(configured.Core.DatabaseLimits, clock,
                expiry ?? clock.GetUtcNow().AddSeconds(configured.Core.DatabaseLimits.Value.QueryDeadlineSeconds), shutdown);
            retainedWork = (request.SessionId, principal, request.Maintenance, work);
            return work;
        }
    }

    internal NativeMaintenanceSessionWork? FindWork(Guid id)
    {
        lock (gate)
        {
            return retainedWork is { } original && original.Id == id ? original.Work : null;
        }
    }

    internal Task JoinStagesAsync()
    {
        NativeMaintenanceSessionWork? original;
        lock (gate)
        {
            original = retainedWork?.Work;
        }
        return original?.CancelAndJoinStagesAsync() ?? Task.CompletedTask;
    }

    internal void RetireWork(Guid? id = null)
    {
        lock (gate)
        {
            if (retainedWork is { } original && (id is null || original.Id == id))
            {
                original.Work.Dispose();
                retainedWork = null;
            }
        }
    }

    internal NativeAnnMaintenanceAdmissionLease Enter(int maximum)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (active >= maximum)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound);
            }
            var lease = new NativeAnnMaintenanceAdmissionLease(this);
            active++;
            return lease;
        }
    }

    internal Task CloseAndJoinAsync()
    {
        lock (gate)
        {
            closed = true;
            if (active == Empty)
            {
                settled.TrySetResult();
            }
            return settled.Task;
        }
    }

    internal void Release()
    {
        lock (gate)
        {
            if (active == Empty)
            {
                throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership);
            }
            active--;
            if (closed && active == Empty)
            {
                settled.TrySetResult();
            }
        }
    }
}
