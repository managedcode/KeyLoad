namespace KeyLoad.Clustering;

/// <summary>Synchronizes the original lease completion and cancellation-observer barrier on its owner's gate.</summary>
internal static class NativeRequestWorkCompletion
{
    internal static Task PrepareAsync(Lock gate, NativeRequestWorkLease lease)
    {
        lock (gate)
        {
            return lease.BeginCompletion();
        }
    }

    internal static Task<NativeRequestWorkOriginalSettlement> CompleteAsync(Lock gate, NativeRequestWorkLease lease,
        Exception? original, Exception? cleanup)
    {
        TaskCompletionSource? start = null;
        lock (gate)
        {
            if (lease.CompletionDriver is null)
            {
                lease.FreezeContext(original, cleanup);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                lease.CompletionDriver = lease.FinishAsync(start.Task, lease.BeginCompletion());
            }
        }
        start?.TrySetResult();
        return lease.Settlement;
    }

    internal static bool TryBeginUnbound(Lock gate, NativeRequestWorkLease lease)
    {
        lock (gate)
        {
            if (lease.Dependencies.Count != EmptyDependencies)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
            }
            if (lease.Completing)
            {
                return false;
            }
            lease.FreezeContext(null, null);
            _ = lease.BeginCompletion();
            lease.CompletionDriver = lease.Settlement;
            return true;
        }
    }
    private const int EmptyDependencies = 0;

    internal static void PrepareDisposal(Lock gate, NativeRequestWorkLease lease, Exception? original, Exception? cleanup)
    {
        lock (gate)
        {
            lease.FreezeContext(original, cleanup);
        }
    }

    internal static void ReleaseCloseObserver(Lock gate, NativeRequestWorkLease lease)
    {
        TaskCompletionSource? released;
        lock (gate)
        {
            released = lease.ReleaseCloseObserver();
        }
        released?.TrySetResult();
    }
}
