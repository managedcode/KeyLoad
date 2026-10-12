namespace KeyLoad.RecoveryTests;

internal sealed class PeerDiscoveryReplayClock(DateTimeOffset initial) : TimeProvider
{
    private readonly Lock sync = new();
    private DateTimeOffset current = initial;
    public override DateTimeOffset GetUtcNow() { lock (sync) { return current; } }
    internal void Set(DateTimeOffset value) { lock (sync) { current = value; } }
}
