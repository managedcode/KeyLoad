using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Borrowed physical-node execution of an already captured original bound analytics cut.</summary>
internal interface INativeUserAnalytics
{
    Task<NativeAnalyticsAggregate> AggregateAsync(AnalyticsAggregateSnapshot snapshot,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken);
}
