using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal sealed class ReplicaVoterReplayWindow(IOptions<ReplicaReplayLimits> replayOptions, int senderIndex, TimeSpan envelopeLifetime)
{
    private const int EmptyCount = 0;

    private readonly ReplicaReplayLimits limits = replayOptions.Value;
    private readonly Dictionary<Guid, ReplicaReplayPool> nonces = [];
    private readonly PriorityQueue<Guid, long> expirations = new();
    private readonly Dictionary<ReplicaReplayPool, int> counts = Enum.GetValues<ReplicaReplayPool>().ToDictionary(pool => pool, _ => EmptyCount);

    internal bool TryAdmit(Guid nonce, long timestamp, long now, ReplicaReplayPool pool, ReplicaRpc method,
        int nodeMaximum, out ReplicaReplayAdmissionFailure failure)
    {
        const int VoterCountSingleItemCount = 1;

        RejectReplay(nonce, now);
        if (counts[pool] >= limits.Capacity(pool))
        {
            expirations.TryPeek(out _, out var oldest);
            failure = new(senderIndex, pool, method, counts[ReplicaReplayPool.Critical], counts[ReplicaReplayPool.Forward],
                counts[ReplicaReplayPool.ReadBarrier], counts[ReplicaReplayPool.DataAppend], limits.Capacity(pool),
                limits.MaximumRetainedNonces(VoterCountSingleItemCount), nodeMaximum, now, oldest);
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
