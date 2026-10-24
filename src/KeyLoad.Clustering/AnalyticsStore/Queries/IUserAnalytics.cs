using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Clustering;

/// <summary>Borrowed physical-node execution of an already captured original bound analytics cut.</summary>
internal interface IUserAnalytics
{
    Task<RuntimeAnalyticsAggregate> AggregateAsync(AnalyticsAggregateSnapshot snapshot,
        IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken);
}
