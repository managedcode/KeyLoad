using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class ReplicaReplayWindow
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, ReplicaVoterReplayWindow> voters;
    private readonly int nodeMaximum;

    internal ReplicaReplayWindow(IReadOnlyList<string> voterIds, IOptions<ReplicaReplayLimits> replayOptions,
        IOptions<ReplicaTransportOptions> transportOptions)
    {
        var limits = replayOptions.Value;
        limits.Validate(voterIds.Count);
        nodeMaximum = limits.MaximumRetainedNonces(voterIds.Count);
        voters = voterIds.Select((voter, index) => (voter, index)).ToDictionary(item => item.voter,
            item => new ReplicaVoterReplayWindow(replayOptions, item.index, transportOptions.Value.EnvelopeLifetime), StringComparer.Ordinal);
    }

    internal void Admit(string sender, Guid nonce, long timestamp, long now, ReplicaReplayPool pool)
    {
        if (!TryAdmit(sender, nonce, timestamp, now, pool, ReplicaRpc.ReadBarrier, out _))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.ReplayCapacityExceeded);
        }
    }

    internal bool TryAdmit(string sender, Guid nonce, long timestamp, long now, ReplicaReplayPool pool,
        ReplicaRpc method, out ReplicaReplayAdmissionFailure failure)
    {
        lock (gate)
        {
            return Voter(sender).TryAdmit(nonce, timestamp, now, pool, method,
                nodeMaximum, out failure);
        }
    }

    internal void RejectReplay(string sender, Guid nonce, long now)
    {
        lock (gate)
        {
            Voter(sender).RejectReplay(nonce, now);
        }
    }

    private ReplicaVoterReplayWindow Voter(string sender)
    {
        if (!voters.TryGetValue(sender, out var voter))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaProtocol.InvalidPeer);
        }

        return voter;
    }
}
