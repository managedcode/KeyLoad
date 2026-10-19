using KeyLoad.AppHost.Features.TestInfrastructure;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Only inspected safe execution facts are retained, including partial failure stages.</summary>
internal sealed class IsolatedKeyLoadFaultChecksReceipt(int node,
    IsolatedKeyLoadFaultChecksIdentity before, DateTimeOffset killStartedAt)
{
    public int Node { get; } = node;
    public IsolatedKeyLoadFaultChecksIdentity Before { get; } = before;
    public DateTimeOffset KillStartedAt { get; } = killStartedAt;
    public DateTimeOffset? KillCompletedAt
    {
        get; internal set;
    }
    public ProcessFaultCommandProtocol.Result? Killed { get; internal set; }
    public IsolatedKeyLoadFaultChecksIdentity? Exited
    {
        get; internal set;
    }
    public IsolatedKeyLoadFaultChecksIdentity? Restored
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
