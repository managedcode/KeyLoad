using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class ReplicaReplayWindow
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, ReplicaVoterReplayWindow> voters;
    private readonly int nodeMaximum;

    internal ReplicaReplayWindow(IReadOnlyList<string> voterIds, IOptions<ReplicaPeerOptions> peerOptions,
        IOptions<ReplicaTransportOptions> transportOptions)
    {
        var limits = peerOptions.Value.ReplayLimits;
        limits.Validate(voterIds.Count);
        nodeMaximum = limits.MaximumRetainedNonces(voterIds.Count);
        voters = voterIds.Select((voter, index) => (voter, index)).ToDictionary(item => item.voter,
            item => new ReplicaVoterReplayWindow(peerOptions, item.index, transportOptions.Value.EnvelopeLifetime), StringComparer.Ordinal);
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

internal sealed class ReplicaVoterReplayWindow(IOptions<ReplicaPeerOptions> peerOptions, int senderIndex, TimeSpan envelopeLifetime)
{
    private readonly ReplicaReplayLimits limits = peerOptions.Value.ReplayLimits;
    private readonly Dictionary<Guid, ReplicaReplayPool> nonces = [];
    private readonly PriorityQueue<Guid, long> expirations = new();
    private readonly Dictionary<ReplicaReplayPool, int> counts = Enum.GetValues<ReplicaReplayPool>().ToDictionary(pool => pool, _ => 0);

    internal bool TryAdmit(Guid nonce, long timestamp, long now, ReplicaReplayPool pool, ReplicaRpc method,
        int nodeMaximum, out ReplicaReplayAdmissionFailure failure)
    {
        RejectReplay(nonce, now);
        if (counts[pool] >= limits.Capacity(pool))
        {
            expirations.TryPeek(out _, out var oldest);
            failure = new(senderIndex, pool, method, counts[ReplicaReplayPool.Critical], counts[ReplicaReplayPool.Forward],
                counts[ReplicaReplayPool.ReadBarrier], counts[ReplicaReplayPool.DataAppend], limits.Capacity(pool),
                limits.MaximumRetainedNonces(1), nodeMaximum, now, oldest);
            return false;
        }

        var lifetime = checked((long)envelopeLifetime.TotalMilliseconds);
        var deadline = checked(timestamp + lifetime);
        nonces.Add(nonce, pool);
        expirations.Enqueue(nonce, deadline);
        counts[pool]++;
        failure = default;
        return true;
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
