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
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                lease.CompletionDriver = lease.FinishAsync(start.Task, lease.BeginCompletion(), original, cleanup);
            }
        }
        start?.TrySetResult();
        return lease.Settlement;
    }

    internal static void CompleteUnbound(Lock gate, NativeRequestWorkLease lease)
    {
        lock (gate)
        {
            if (lease.Dependencies.Count != EmptyDependencies)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, NativeRequestWorkLimits.LeaseIdentityMessage);
            }
            if (lease.Completing)
            {
                return;
            }
            _ = lease.BeginCompletion();
        }
        var outcome = lease.Finish(null, null);
        NativeRequestWorkSettlement.Rethrow(outcome.CleanupOrJoinFailure);
    }
    private const int EmptyDependencies = 0;

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
