namespace KeyLoad.Replication;

internal sealed class PeerDiscoveryReplay(int capacity, long timestampWindowMilliseconds)
{
    private readonly Lock gate = new();
    private readonly HashSet<Guid> nonces = [];
    private readonly PriorityQueue<Guid, long> expirations = new();

    internal bool Admit(Guid nonce, long timestamp, long now)
    {
        lock (gate)
        {
            while (expirations.TryPeek(out var expired, out var deadline) && deadline < now)
            {
                expirations.Dequeue();
                nonces.Remove(expired);
            }
            if (nonces.Contains(nonce))
            { return false; }
            if (nonces.Count >= capacity)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, PeerDiscoveryProtocol.ReplayCapacityExceeded); }
            var expires = checked(timestamp + timestampWindowMilliseconds);
            nonces.Add(nonce);
            expirations.Enqueue(nonce, expires);
            return true;
        }
    }
}
