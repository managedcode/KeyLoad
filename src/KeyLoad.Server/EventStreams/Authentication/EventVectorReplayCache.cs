using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorReplayCache : IDisposable
{
    private readonly TimeProvider clock;
    private readonly OrleansMembershipOptions options;
    private readonly Lock gate = new();
    private readonly Dictionary<string, (long Timestamp, DateTimeOffset Expires)> entries = new(StringComparer.Ordinal);
    private bool disposed;

    internal EventVectorReplayCache(TimeProvider clock, IOptions<OrleansMembershipOptions> options)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        options.Value.Validate();
        this.clock = clock;
        this.options = options.Value;
    }

    internal bool TryUse(string nonce, DateTimeOffset expires)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var stamp = clock.GetTimestamp();
            var now = clock.GetUtcNow();
            foreach (var key in entries.Where(pair => pair.Value.Expires <= now
                && clock.GetElapsedTime(pair.Value.Timestamp, stamp) >= options.ReplayLifetime)
                .Select(pair => pair.Key).ToArray())
            {
                entries.Remove(key);
            }
            if (expires <= now || entries.Count >= options.ReplayNonceCapacity || entries.ContainsKey(nonce))
            {
                return false;
            }
            entries.Add(nonce, (stamp, expires));
            return true;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            entries.Clear();
        }
    }
}
