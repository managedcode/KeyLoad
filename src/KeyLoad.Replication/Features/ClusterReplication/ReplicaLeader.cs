namespace KeyLoad.Replication;

internal sealed class ReplicaLeader(ReplicaState state, ReplicaFollowerSender followers) : IDisposable
{
    private readonly SemaphoreSlim rounds = new(1, 1);

    /// <inheritdoc />
    public void Dispose() => rounds.Dispose();

    internal async Task<OperationResult> SubmitAsync(ReplicatedOperation operation, CancellationToken cancellationToken)
    {
        await rounds.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entry = await state.LockedAsync(() =>
            {
                state.RequireReadyLeader();
                var next = new ReplicaEntry(checked(state.Log.State.LastIndex + 1), state.Log.State.Term,
                    operation with { EvaluatedAt = state.Clock.GetUtcNow() });
                state.Log.Append([next]);
                return next;
            }, cancellationToken).ConfigureAwait(false);
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
            return state.Materializer.Database.ResolveOutcome(entry.Operation!);
        }
        finally { rounds.Release(); }
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
        if (!await rounds.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            var term = await state.LockedAsync(() =>
            { return state.Role == ReplicaRole.Leader ? state.Log.State.Term : 0; }, cancellationToken).ConfigureAwait(false);
            if (term > 0)
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
        var acknowledgements = 1;
        while (pending.Count > 0 && acknowledgements < state.Configuration.Majority)
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
            var cut = matches[state.Configuration.Majority - 1];
            if (cut > state.Log.State.CommittedIndex && state.Log.TermAt(cut) == term)
            {
                state.Materializer.Commit(cut);
            }
            return acknowledgements >= state.Configuration.Majority;
        }, cancellationToken).ConfigureAwait(false);
    }
}
