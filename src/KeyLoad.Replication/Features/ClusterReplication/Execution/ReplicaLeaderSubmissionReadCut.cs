namespace KeyLoad.Replication;

/// <summary>Borrows the leader's existing exclusive round ownership to join its captured submission cut.</summary>
internal static class ReplicaLeaderSubmissionReadCut
{
    internal static async Task<long> RequireAsync(ReplicaState state, ReplicaFollowerSender followers,
        CancellationToken cancellationToken)
    {
        var original = await state.LockedAsync(() =>
        {
            state.RequireReadyLeader();
            return (Term: state.Log.State.Term, Tail: state.Log.State.LastIndex);
        }, cancellationToken).ConfigureAwait(false);
        long cut;
        while (true)
        {
            var quorum = await ReplicaLeaderRound.ExecuteAsync(state, followers, original.Term,
                ReplicaReadRoundPurpose.Control, cancellationToken).ConfigureAwait(false);
            cut = await state.LockedAsync(() =>
            {
                state.RequireLeader(original.Term);
                return state.Log.State.CommittedIndex;
            }, cancellationToken).ConfigureAwait(false);
            if (quorum && cut >= original.Tail)
            {
                break;
            }
            await Task.Delay(state.Configuration.HeartbeatInterval, state.Clock, cancellationToken).ConfigureAwait(false);
        }
        await state.Materializer.WaitForApplyAsync(cut, cancellationToken).ConfigureAwait(false);
        await state.LockedAsync(() =>
        {
            state.RequireLeader(original.Term);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        return original.Term;
    }
}
