namespace KeyLoad.Replication;

internal sealed class ReplicaElection(ReplicaState state, ReplicaRpcClient rpc)
{
    internal Task<VoteReply> ReceiveAsync(VoteRequest request, CancellationToken cancellationToken)
        => state.LockedAsync(() => Receive(request), cancellationToken);

    private VoteReply Receive(VoteRequest request)
    {
        if (request.Term < 1 || request.LastIndex < 0 || request.LastTerm < 0 || request.LastTerm > request.Term
            || request.LastIndex == 0 && request.LastTerm != 0 || request.LastIndex > 0 && request.LastTerm == 0
            || !state.Configuration.VoterIds.Contains(request.CandidateId, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidPeer);
        }
        state.ObserveTerm(request.Term);
        var durable = state.Log.State;
        var lastTerm = state.Log.TermAt(durable.LastIndex);
        var fresh = request.LastTerm > lastTerm || request.LastTerm == lastTerm && request.LastIndex >= durable.LastIndex;
        var grant = request.Term == durable.Term && fresh && (durable.VotedFor is null || durable.VotedFor == request.CandidateId);
        if (grant)
        {
            state.Log.SaveTermAndVote(request.Term, request.CandidateId);
            state.ResetElection();
        }
        return new(state.Log.State.Term, grant);
    }

    internal async Task CampaignAsync(CancellationToken cancellationToken)
    {
        var request = await state.LockedAsync(() =>
        {
            if (state.Role == ReplicaRole.Leader || !state.ElectionDue)
            {
                return null;
            }
            state.Log.SaveTermAndVote(checked(state.Log.State.Term + 1), state.Configuration.LocalId);
            state.Role = ReplicaRole.Candidate;
            state.LeaderId = null;
            state.LeaderReadyIndex = 0;
            state.ResetElection();
            var durable = state.Log.State;
            return new VoteRequest(state.Configuration.LocalId, durable.Term, durable.LastIndex, state.Log.TermAt(durable.LastIndex));
        }, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return;
        }
        var votes = await Task.WhenAll(state.Configuration.VoterIds.Where(voter => voter != state.Configuration.LocalId)
            .Select(voter => AskAsync(voter, request, cancellationToken))).ConfigureAwait(false);
        await state.LockedAsync(() => Win(request.Term, votes.Count(granted => granted) + 1), cancellationToken).ConfigureAwait(false);
    }

    private bool Win(long term, int votes)
    {
        if (state.Role != ReplicaRole.Candidate || state.Log.State.Term != term || votes < state.Configuration.Majority)
        {
            return false;
        }
        state.Role = ReplicaRole.Leader;
        state.LeaderId = state.Configuration.LocalId;
        var next = checked(state.Log.State.LastIndex + 1);
        state.Progress.Clear();
        foreach (var voter in state.Configuration.VoterIds)
        {
            state.Progress[voter] = new(next, 0);
        }
        state.Log.Append([new(next, term, null)]);
        state.LeaderReadyIndex = next;
        return true;
    }

    private async Task<bool> AskAsync(string voter, VoteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await rpc.InvokeAsync<VoteRequest, VoteReply>(voter, ReplicaRpc.RequestVote, request, cancellationToken).ConfigureAwait(false);
            return await state.LockedAsync(() =>
            {
                state.ObserveTerm(response.Term);
                return response.Granted && response.Term == request.Term && state.Log.State.Term == request.Term;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (ReplicaRpcClient.Unavailable(error)) { return false; }
    }
}
