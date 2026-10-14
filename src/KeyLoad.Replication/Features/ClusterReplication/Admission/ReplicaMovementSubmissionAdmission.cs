namespace KeyLoad.Replication;

internal static class ReplicaMovementSubmissionAdmission
{
    internal static async Task ValidateAsync(ReplicaState state, ReplicatedOperation operation,
        CancellationToken cancellationToken)
    {
        await state.LockedAsync(() =>
        {
            state.RequireReadyLeader();
            return true;
        }, cancellationToken).ConfigureAwait(false);
        try
        {
            state.Materializer.Database.ValidatePartitionMovementSubmissionAdmission(operation);
        }
        catch (KeyLoadException refusal) when (refusal.Code == ErrorCode.Conflict)
        {
            await state.LockedAsync(() =>
            {
                state.RequireReadyLeader();
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(refusal).Throw();
                return false;
            }, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
