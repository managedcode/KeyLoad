namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKeyLoadFaultChecksPhaseReceipt(int phase, int killedNode, Guid commandId, string kind)
{
    public int Phase { get; } = phase;
    public int KilledNode { get; } = killedNode;
    public Guid CommandId { get; } = commandId;
    public string Kind { get; } = kind;
    public bool NativeMcpAuthentication503
    {
        get; internal set;
    }
    public string? WriteRejection
    {
        get; internal set;
    }
    public long? ResolvedPosition
    {
        get; internal set;
    }
    public IsolatedKeyLoadFaultChecksMembership[] Restored { get; internal set; } = [];
    public bool Complete
    {
        get; internal set;
    }
}
