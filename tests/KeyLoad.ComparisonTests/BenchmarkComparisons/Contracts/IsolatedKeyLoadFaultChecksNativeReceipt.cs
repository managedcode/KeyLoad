namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Only inspected safe native facts are retained, including partial failure stages.</summary>
internal sealed class IsolatedKeyLoadFaultChecksNativeReceipt(int node,
    IsolatedKeyLoadFaultChecksNativeIdentity before, DateTimeOffset killStartedAt)
{
    public int Node { get; } = node;
    public IsolatedKeyLoadFaultChecksNativeIdentity Before { get; } = before;
    public DateTimeOffset KillStartedAt { get; } = killStartedAt;
    public DateTimeOffset? KillCompletedAt
    {
        get; internal set;
    }
    public IsolatedKeyLoadFaultChecksNativeIdentity? Exited
    {
        get; internal set;
    }
    public IsolatedKeyLoadFaultChecksNativeIdentity? Restored
    {
        get; internal set;
    }
    public DateTimeOffset? RestoredAt
    {
        get; internal set;
    }
    public bool CleanupFailed
    {
        get; internal set;
    }
}
