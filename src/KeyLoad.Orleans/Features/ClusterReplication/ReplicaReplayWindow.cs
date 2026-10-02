using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal sealed class ReplicaReplayWindow
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, ReplicaVoterReplayWindow> voters;

    internal ReplicaReplayWindow(IReadOnlyList<string> voterIds, ReplicaReplayLimits limits)
    {
        limits.Validate(voterIds.Count);
        voters = voterIds.ToDictionary(voter => voter, _ => new ReplicaVoterReplayWindow(limits), StringComparer.Ordinal);
    }

    internal void Admit(string sender, Guid nonce, long timestamp, long now, ReplicaReplayPool pool)
    {
        lock (gate)
        {
            Voter(sender).Admit(nonce, timestamp, now, pool);
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

internal sealed class ReplicaVoterReplayWindow(ReplicaReplayLimits limits)
{
    private readonly Dictionary<Guid, ReplicaReplayPool> nonces = [];
    private readonly PriorityQueue<Guid, long> expirations = new();
    private readonly Dictionary<ReplicaReplayPool, int> counts = Enum.GetValues<ReplicaReplayPool>().ToDictionary(pool => pool, _ => 0);

    internal void Admit(Guid nonce, long timestamp, long now, ReplicaReplayPool pool)
    {
        RejectReplay(nonce, now);
        if (counts[pool] >= limits.Capacity(pool))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.ReplayCapacityExceeded);
        }

        var lifetime = checked((long)ReplicaTransportProtocol.EnvelopeLifetime.TotalMilliseconds);
        var deadline = checked(timestamp + lifetime);
        nonces.Add(nonce, pool);
        expirations.Enqueue(nonce, deadline);
        counts[pool]++;
    }

    internal void RejectReplay(Guid nonce, long now)
    {
        while (expirations.TryPeek(out var expired, out var deadline) && deadline < now)
        {
            expirations.Dequeue();
            var releasedPool = nonces[expired];
            nonces.Remove(expired);
            counts[releasedPool]--;
        }

        // Nonces are shared across methods and pools; capacity errors never hide an actual replay.
        if (nonces.ContainsKey(nonce))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaProtocol.InvalidPeer);
        }
    }
}
