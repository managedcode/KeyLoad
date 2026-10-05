namespace KeyLoad.Replication;

internal sealed class ReplicaActivityTracker
{
    private const int NoActiveOperations = 0;

    private readonly Lock gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int active;
    private bool closed;

    internal IDisposable Enter()
    {
        lock (gate)
        {
            if (closed)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
            }
            active++;
            return new Lease(this);
        }
    }

    internal Task Close()
    {
        lock (gate)
        {
            closed = true;
            if (active == NoActiveOperations)
            {
                drained.TrySetResult();
            }
            return drained.Task;
        }
    }

    private void Release()
    {
        lock (gate)
        {
            active--;
            if (closed && active == NoActiveOperations)
            {
                drained.TrySetResult();
            }
        }
    }

    private sealed class Lease(ReplicaActivityTracker owner) : IDisposable
    {
        private ReplicaActivityTracker? current = owner;
        /// <inheritdoc />
        public void Dispose() => Interlocked.Exchange(ref current, null)?.Release();
    }
}
