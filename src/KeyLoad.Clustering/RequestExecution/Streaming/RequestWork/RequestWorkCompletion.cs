namespace KeyLoad.Clustering;

/// <summary>Synchronizes the original lease's first failure context and async settlement on its owner's gate.</summary>
internal static class RequestWorkCompletion
{
    internal static Task PrepareAsync(Lock gate, RequestWorkLease lease)
    {
        lock (gate)
        {
            return lease.BeginCompletion();
        }
    }

    internal static Task<RequestWorkOriginalSettlement> CompleteAsync(Lock gate, RequestWorkLease lease,
        Exception? original, Exception? cleanup)
    {
        TaskCompletionSource? start = null;
        lock (gate)
        {
            if (lease.CompletionDriver is null)
            {
                lease.FreezeContext(original, cleanup);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _ = lease.BeginCompletion();
                lease.CompletionDriver = lease.FinishAsync(start.Task);
            }
        }
        start?.TrySetResult();
        return lease.Settlement;
    }

    internal static bool TryBeginUnbound(Lock gate, RequestWorkLease lease)
    {
        lock (gate)
        {
            if (lease.Dependencies.Count != EmptyDependencies)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, RequestWorkLimits.LeaseIdentityMessage);
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

    internal static void PrepareDisposal(Lock gate, RequestWorkLease lease, Exception? original, Exception? cleanup)
    {
        lock (gate)
        {
            lease.FreezeContext(original, cleanup);
        }
    }

}
