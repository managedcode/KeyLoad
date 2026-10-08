namespace KeyLoad.Server.Features.Search;

internal sealed class NativeAnnMaintenanceAdmissions
{
    private const int Empty = 0;
    private readonly Lock gate = new();
    private readonly TaskCompletionSource settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool closed;
    private int active;

    internal NativeAnnMaintenanceAdmissionLease Enter(int maximum)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (active >= maximum)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, NativeAnnProtocol.Bound); }
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
            { settled.TrySetResult(); }
            return settled.Task;
        }
    }

    internal void Release()
    {
        lock (gate)
        {
            if (active == Empty)
            { throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Ownership); }
            active--;
            if (closed && active == Empty)
            { settled.TrySetResult(); }
        }
    }
}

internal sealed class NativeAnnMaintenanceAdmissionLease(NativeAnnMaintenanceAdmissions admissions) : IDisposable
{
    private NativeAnnMaintenanceAdmissions? owner = admissions;
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
}
