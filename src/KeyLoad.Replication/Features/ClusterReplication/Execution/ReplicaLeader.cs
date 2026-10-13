using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

internal sealed class ReplicaLeader(ReplicaState state, ReplicaFollowerSender followers) : IDisposable
{
    private const int ExclusiveRoundPermit = 1;
    private const int ContiguousIndexStep = 1;
    private const int UnelectedTerm = 0;
    private const int LocalAcknowledgementCount = 1;
    private const int NoPendingFollowers = 0;
    private const int MajorityArrayIndexAdjustment = 1;

    private const int HeartbeatAdmissionWaitMilliseconds = 0;
    private readonly SemaphoreSlim rounds = new(ExclusiveRoundPermit, ExclusiveRoundPermit);

    /// <inheritdoc />
    public void Dispose() => rounds.Dispose();

    internal async Task<OperationResult> SubmitAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        operation = ReplicaOperationAuthority.Verify(operation, state.Materializer.Database)!;
        var boundary = new ReplicaSubmissionBoundary();
        OperationResult? terminal = null;
        try
        {
            await rounds.WaitAsync(cancellationToken).ConfigureAwait(false);
            Exception? primary = null;
            try
            {
                terminal = await SubmitOwnedAsync(operation, boundary, cancellationToken).ConfigureAwait(false);
                return terminal;
            }
            catch (Exception error) { primary = error; throw; }
            finally { ReleaseRound(primary); }
        }
        catch (Exception error) when ((terminal is not null || boundary.IsStarted) && CqrsRuntimeFailures.FindFatal(error) is null)
        {
            if (terminal is not null)
            { return terminal; }
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite, error);
        }
    }

    private void ReleaseRound(Exception? primary)
    {
        try
        { rounds.Release(); }
        catch (Exception cleanup)
        {
            state.Poison(cleanup);
            if (primary is not null)
            { throw new AggregateException(primary, cleanup); }
            throw;
        }
    }

    private async Task<OperationResult> SubmitOwnedAsync(ReplicatedOperation operation,
        ReplicaSubmissionBoundary boundary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (operation.Kind == OperationKind.PartitionMovementPhase)
        {
            await ReplicaMovementSubmissionAdmission.ValidateAsync(state, operation, cancellationToken)
                .ConfigureAwait(false);
        }
        if (state.Materializer.Database.TryReadOrdinaryDocumentSubmission(operation) is { } command)
        {
            await state.LockedAsync(() =>
            { state.RequireReadyLeader(); return true; }, cancellationToken).ConfigureAwait(false);
            try
            { state.Materializer.Database.ValidateSubmissionAdmission(operation, command); }
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
        cancellationToken.ThrowIfCancellationRequested();
        var admission = await state.LockedAsync(() => AppendOwned(operation, boundary, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (admission.Outcome is { } originalOutcome)
        { return originalOutcome; }
        var entry = admission.Entry ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
        while (true)
        {
            await RoundAsync(entry.Term, ReplicaReadRoundPurpose.Control, cancellationToken).ConfigureAwait(false);
            var committed = await state.LockedAsync(() =>
            { state.RequireLeader(entry.Term); return state.Log.State.CommittedIndex >= entry.Index; }, cancellationToken).ConfigureAwait(false);
            if (committed)
            {
                break;
            }
            await Task.Delay(state.Configuration.HeartbeatInterval, state.Clock, cancellationToken).ConfigureAwait(false);
        }
        await state.Materializer.WaitForApplyAsync(entry.Index, cancellationToken).ConfigureAwait(false);
        var outcome = state.Materializer.Database.ResolveAppliedSubmissionOutcome(entry.Operation!);
        if (admission.SourcePhaseId != Guid.Empty && outcome.Error is null)
        {
            state.Materializer.Database.RequireEventVectorSourceAdmitOutcome(entry.Operation!,
                admission.OriginalRequestId, admission.SourcePhaseId, entry.Index, cancellationToken);
            return outcome with
            {
                NativeValue = EventVectorSourceAdmitAppendResult.FromAppliedEntry(entry,
                admission.OriginalRequestId, admission.SourcePhaseId, outcome)
            };
        }
        return admission.OriginalRequestId != Guid.Empty && outcome.Error is null
            ? outcome with
            {
                NativeValue = EventVectorFirstAdmitAppendResult.FromAppliedEntry(entry,
                admission.OriginalRequestId, outcome)
            }
            : outcome;
    }

    private (ReplicaEntry? Entry, OperationResult? Outcome, Guid OriginalRequestId, Guid SourcePhaseId)
        AppendOwned(ReplicatedOperation operation, ReplicaSubmissionBoundary boundary, CancellationToken cancellationToken)
    {
        state.RequireReadyLeader();
        using var firstAdmit = state.Materializer.Database.OpenEventVectorFirstAdmitProbe(operation, cancellationToken);
        if (firstAdmit?.OriginalOutcome is { } original)
        { return (null, original, Guid.Empty, Guid.Empty); }
        using var sourceAdmit = state.Materializer.Database.OpenEventVectorSourceAdmitProbe(operation, cancellationToken);
        if (sourceAdmit?.OriginalOutcome is { } sourceOriginal)
        { return (null, sourceOriginal, Guid.Empty, Guid.Empty); }
        ReplicaFirstEventVectorAdmit.RequireAbsent(state, firstAdmit, operation);
        ReplicaEventVectorSourceAdmit.RequireAbsent(state, sourceAdmit, operation);
        var admitted = state.Materializer.Database.PrepareQueueRetryOperation(operation with { EvaluatedAt = state.Clock.GetUtcNow() });
        var next = new ReplicaEntry(checked(state.Log.State.LastIndex + ContiguousIndexStep), state.Log.State.Term, admitted);
        var nativeLog = state.Log as DurableReplicaLog
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
        nativeLog.Append([next], boundary, cancellationToken);
        return (next, null, firstAdmit?.OriginalParentRequestId ?? sourceAdmit?.OriginalParentRequestId ?? Guid.Empty,
            sourceAdmit?.OriginalSourcePhaseCommandId ?? Guid.Empty);
    }

    internal async Task<ReadBarrierReceipt> BarrierAsync(ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
    {
        await rounds.WaitAsync(cancellationToken).ConfigureAwait(false);
        long term;
        long cut;
        try
        {
            term = await state.LockedAsync(() => { state.RequireReadyLeader(); return state.Log.State.Term; }, cancellationToken).ConfigureAwait(false);
            if (!await RoundAsync(term, purpose, cancellationToken).ConfigureAwait(false))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaProtocol.NoLeader);
            }
            cut = await state.LockedAsync(() =>
            { state.RequireLeader(term); return state.Log.State.CommittedIndex; }, cancellationToken).ConfigureAwait(false);
        }
        finally { rounds.Release(); }
        await state.Materializer.WaitForApplyAsync(cut, cancellationToken).ConfigureAwait(false);
        await state.LockedAsync(() => { state.RequireLeader(term); return true; }, cancellationToken).ConfigureAwait(false);
        return new(state.Configuration.Incarnation, cut, term);
    }

    internal async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        if (!await rounds.WaitAsync(HeartbeatAdmissionWaitMilliseconds, cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            var term = await state.LockedAsync(() =>
            { return state.Role == ReplicaRole.Leader ? state.Log.State.Term : UnelectedTerm; }, cancellationToken).ConfigureAwait(false);
            if (term > UnelectedTerm)
            {
                await RoundAsync(term, ReplicaReadRoundPurpose.Control, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { rounds.Release(); }
    }

    private async Task<bool> RoundAsync(long term, ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
    {
        var pending = state.Configuration.VoterIds.Where(voter => voter != state.Configuration.LocalId)
            .Select(voter => followers.SynchronizeAsync(voter, term, purpose, cancellationToken)).ToList();
        var acknowledgements = LocalAcknowledgementCount;
        while (pending.Count > NoPendingFollowers && acknowledgements < state.Configuration.Majority)
        {
            var completed = await Task.WhenAny(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
            pending.Remove(completed);
            if (await completed.ConfigureAwait(false))
            {
                acknowledgements++;
            }
        }
        return await state.LockedAsync(() =>
        {
            state.RequireLeader(term);
            var matches = state.Configuration.VoterIds.Select(voter => voter == state.Configuration.LocalId
                ? state.Log.State.LastIndex : state.Progress[voter].MatchedIndex).OrderDescending().ToArray();
            var cut = matches[state.Configuration.Majority - MajorityArrayIndexAdjustment];
            if (cut > state.Log.State.CommittedIndex && state.Log.TermAt(cut) == term)
            {
                state.Materializer.Commit(cut);
            }
            return acknowledgements >= state.Configuration.Majority;
        }, cancellationToken).ConfigureAwait(false);
    }
}
