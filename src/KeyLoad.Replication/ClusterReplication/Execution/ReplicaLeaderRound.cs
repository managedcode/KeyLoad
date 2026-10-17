namespace KeyLoad.Replication;

internal static class ReplicaLeaderRound
{
    private const int LocalAcknowledgementCount = 1;
    private const int NoPendingFollowers = 0;
    private const int MajorityArrayIndexAdjustment = 1;

    internal static async Task<bool> ExecuteAsync(ReplicaState state, ReplicaFollowerSender followers,
        long term, ReplicaReadRoundPurpose purpose, CancellationToken cancellationToken)
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
