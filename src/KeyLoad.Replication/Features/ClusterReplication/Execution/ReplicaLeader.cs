using ManagedCode.Communication.CQRS;

namespace KeyLoad.Replication;

internal sealed class ReplicaLeader(ReplicaState state, ReplicaFollowerSender followers) : IDisposable
{
    private const int ExclusiveRoundPermit = 1;
    private const int UnelectedTerm = 0;

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
            catch (Exception error)
            {
                primary = error;
                throw;
            }
            finally
            {
                ReleaseRound(primary);
            }
        }
        catch (Exception error) when ((terminal is not null || boundary.IsStarted) && CqrsRuntimeFailures.FindFatal(error) is null)
        {
            if (terminal is not null)
            {
                return terminal;
            }
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, ReplicaProtocol.InterruptedWrite, error);
        }
    }

    private void ReleaseRound(Exception? primary)
    {
        try
        {
            rounds.Release();
        }
        catch (Exception cleanup)
        {
            state.Poison(cleanup);
            if (primary is not null)
            {
                throw new AggregateException(primary, cleanup);
            }
            throw;
        }
    }

    private async Task<OperationResult> SubmitOwnedAsync(ReplicatedOperation operation,
        ReplicaSubmissionBoundary boundary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await ReplicaLeaderSubmissionAdmission.ValidateAsync(state, operation, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var admission = await state.LockedAsync(() => ReplicaLeaderSubmissionAdmission.AppendOwned(state, operation, boundary, cancellationToken), cancellationToken).ConfigureAwait(false);
        if (admission.Outcome is { } originalOutcome)
        {
            return originalOutcome;
        }
        var entry = admission.Entry ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ReplicaProtocol.CorruptLog);
        while (true)
        {
            await RoundAsync(entry.Term, ReplicaReadRoundPurpose.Control, cancellationToken).ConfigureAwait(false);
            var committed = await state.LockedAsync(() =>
            {
                state.RequireLeader(entry.Term);
                return state.Log.State.CommittedIndex >= entry.Index;
            }, cancellationToken).ConfigureAwait(false);
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

    internal async Task<ReadBarrierReceipt> BarrierAsync(ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
    {
        var diagnosticStage = ReplicaReadFailureMetadata.LeaderRound;
        try
        {
            await rounds.WaitAsync(cancellationToken).ConfigureAwait(false);
            long term;
            long cut;
            try
            {
                diagnosticStage = ReplicaReadFailureMetadata.LeaderReady;
                term = await state.LockedAsync(() =>
                {
                    state.RequireReadyLeader();
                    return state.Log.State.Term;
                }, cancellationToken).ConfigureAwait(false);
                diagnosticStage = ReplicaReadFailureMetadata.LeaderQuorum;
                while (!await RoundAsync(term, purpose, cancellationToken).ConfigureAwait(false))
                {
                    await Task.Delay(state.Configuration.HeartbeatInterval, state.Clock, cancellationToken).ConfigureAwait(false);
                }
                diagnosticStage = ReplicaReadFailureMetadata.LeaderTerm;
                cut = await state.LockedAsync(() =>
                {
                    state.RequireLeader(term);
                    return state.Log.State.CommittedIndex;
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                rounds.Release();
            }
            diagnosticStage = ReplicaReadFailureMetadata.Apply;
            await state.Materializer.WaitForApplyAsync(cut, cancellationToken).ConfigureAwait(false);
            diagnosticStage = ReplicaReadFailureMetadata.LeaderTerm;
            await state.LockedAsync(() =>
            {
                state.RequireLeader(term);
                return true;
            }, cancellationToken).ConfigureAwait(false);
            return new(state.Configuration.Incarnation, cut, term);
        }
        catch (Exception error) when (error is KeyLoadException or OperationCanceledException)
        {
            ReplicaReadFailureMetadata.MarkStage(error, diagnosticStage);
            throw;
        }
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
            {
                return state.Role == ReplicaRole.Leader ? state.Log.State.Term : UnelectedTerm;
            }, cancellationToken).ConfigureAwait(false);
            if (term > UnelectedTerm)
            {
                await RoundAsync(term, ReplicaReadRoundPurpose.Control, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            rounds.Release();
        }
    }

    private Task<bool> RoundAsync(long term, ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
        => ReplicaLeaderRound.ExecuteAsync(state, followers, term, purpose, cancellationToken);

}
