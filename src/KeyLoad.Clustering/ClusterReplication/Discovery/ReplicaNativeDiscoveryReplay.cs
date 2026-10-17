using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

/// <summary>Retains bounded native discovery nonces independently from consensus RPC replay pools.</summary>
internal sealed class ReplicaNativeDiscoveryReplay(IOptions<OrleansMembershipOptions> options, TimeProvider clock)
{
    private readonly Lock sync = new();
    private readonly Dictionary<(string Voter, Guid Nonce), long> retained = [];
    private readonly PriorityQueue<(string Voter, Guid Nonce), long> expirations = new();

    internal void Admit(ReplicaNativeDiscoveryRequest request)
    {
        lock (sync)
        {
            var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            while (expirations.TryPeek(out var expired, out var expiry) && expiry < now)
            {
                expirations.Dequeue();
                retained.Remove(expired);
            }
            var key = (request.CallerVoter, request.Nonce);
            if (retained.ContainsKey(key))
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaTransportProtocol.InvalidDiscovery);
            }
            if (retained.Count >= options.Value.ReplayNonceCapacity)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.ReplayCapacityExceeded);
            }
            var until = Math.Max(request.ExpiresAt.ToUnixTimeMilliseconds(),
                checked(now + (long)options.Value.ReplayLifetime.TotalMilliseconds));
            retained.Add(key, until);
            expirations.Enqueue(key, until);
        }
    }
}
