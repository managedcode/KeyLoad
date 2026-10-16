namespace KeyLoad.Replication;

internal static class ReplicaLeaderSubmissionAdmission
{
    private const int ContiguousIndexStep = 1;

    internal static async Task ValidateAsync(ReplicaState state, ReplicatedOperation operation,
        CancellationToken cancellationToken)
    {
        if (operation.Kind == OperationKind.PartitionMovementPhase)
        {
            await ReplicaMovementSubmissionAdmission.ValidateAsync(state, operation, cancellationToken)
                .ConfigureAwait(false);
        }
        if (state.Materializer.Database.TryReadOrdinaryDocumentSubmission(operation) is { } command)
        {
            await state.LockedAsync(() =>
            {
                state.RequireReadyLeader();
                return true;
            }, cancellationToken).ConfigureAwait(false);
            try
            {
                state.Materializer.Database.ValidateSubmissionAdmission(operation, command);
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

    internal static (ReplicaEntry? Entry, OperationResult? Outcome, Guid OriginalRequestId, Guid SourcePhaseId)
        AppendOwned(ReplicaState state, ReplicatedOperation operation, ReplicaSubmissionBoundary boundary, CancellationToken cancellationToken)
    {
        state.RequireReadyLeader();
        using var firstAdmit = state.Materializer.Database.OpenEventVectorFirstAdmitProbe(operation, cancellationToken);
        if (firstAdmit?.OriginalOutcome is { } original)
        {
            return (null, original, Guid.Empty, Guid.Empty);
        }
        using var sourceAdmit = state.Materializer.Database.OpenEventVectorSourceAdmitProbe(operation, cancellationToken);
        if (sourceAdmit?.OriginalOutcome is { } sourceOriginal)
        {
            return (null, sourceOriginal, Guid.Empty, Guid.Empty);
        }
        var retained = state.Materializer.Database.ReadRetainedSubmissionOutcome(operation);
        if (retained is not null)
        {
            return (null, retained, Guid.Empty, Guid.Empty);
        }
        ReplicaFirstEventVectorAdmit.RequireAbsent(state, firstAdmit, operation);
        ReplicaEventVectorSourceAdmit.RequireAbsent(state, sourceAdmit, operation);
        var admitted = state.Materializer.Database.PrepareQueueRetryOperation(operation with
        {
            EvaluatedAt = state.Clock.GetUtcNow()
        });
        var next = new ReplicaEntry(checked(state.Log.State.LastIndex + ContiguousIndexStep), state.Log.State.Term, admitted);
        var nativeLog = state.Log as DurableReplicaLog
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
        nativeLog.Append([next], boundary, cancellationToken);
        return (next, null, firstAdmit?.OriginalParentRequestId ?? sourceAdmit?.OriginalParentRequestId ?? Guid.Empty,
            sourceAdmit?.OriginalSourcePhaseCommandId ?? Guid.Empty);
    }

}
