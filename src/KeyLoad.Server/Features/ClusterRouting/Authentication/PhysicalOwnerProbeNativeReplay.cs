using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeNativeReplay(TimeProvider clock, IOptions<OrleansMembershipOptions> options)
    : IDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, (long Timestamp, DateTimeOffset Expires)> entries = new(StringComparer.Ordinal);
    internal bool TryUse(string nonce, DateTimeOffset expires)
    {
        lock (gate)
        {
            var stamp = clock.GetTimestamp();
            var now = clock.GetUtcNow();
            foreach (var key in entries.Where(pair => pair.Value.Expires <= now
                && clock.GetElapsedTime(pair.Value.Timestamp, stamp) >= options.Value.ReplayLifetime)
                .Select(pair => pair.Key).ToArray())
            { entries.Remove(key); }
            if (entries.Count >= options.Value.ReplayNonceCapacity || entries.ContainsKey(nonce))
            { return false; }
            entries.Add(nonce, (stamp, expires));
            return true;
        }
    }
    public void Dispose() { lock (gate) { entries.Clear(); } }
}
