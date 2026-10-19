using KeyLoad.Core;

namespace KeyLoad.Clustering;

/// <summary>Same-process execution output and original cut; never a public or inter-grain payload.</summary>
internal sealed class RuntimeAnalyticsAggregate(AnalyticsAggregateResult result, AnalyticsAggregateSnapshot snapshot) : IDisposable
{
    private AnalyticsAggregateSnapshot? captured = snapshot;
    internal AnalyticsAggregateResult Result { get; } = result;
    internal AnalyticsAggregateSnapshot Snapshot => captured ?? throw new ObjectDisposedException(nameof(RuntimeAnalyticsAggregate));

    // The runtime retains bounded original tasks; release their potentially large private source cut after publication.
    public void Dispose() => captured = null;
}
