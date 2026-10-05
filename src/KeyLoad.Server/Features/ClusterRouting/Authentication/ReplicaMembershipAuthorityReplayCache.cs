using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class ReplicaMembershipAuthorityReplayCache(TimeProvider clock,
    IOptions<OrleansMembershipOptions> options) : IDisposable
{
    private readonly OrleansMembershipOptions settings = options.Value;
    private readonly Lock sync = new();
    private readonly Dictionary<string, long> live = new(StringComparer.Ordinal);

    internal bool TryUse(string nonce)
    {
        var now = clock.GetTimestamp();
        lock (sync)
        {
            foreach (var expired in live.Where(pair => clock.GetElapsedTime(pair.Value, now) >= settings.ReplayLifetime)
                         .Select(pair => pair.Key).ToArray())
            { live.Remove(expired); }
            if (live.Count >= settings.ReplayNonceCapacity || live.ContainsKey(nonce))
            { return false; }
            live.Add(nonce, now);
            return true;
        }
    }

    public void Dispose()
    {
        lock (sync)
        { live.Clear(); }
    }
}
