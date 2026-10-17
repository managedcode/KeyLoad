using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal StreamPage ReadStreamBound(string principalId, ReadStreamRequest request,
        PhysicalShardRecord configuredOwner, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(configuredOwner);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return ReadStreamTraversal(principalId, request, configuredOwner, budget);
    }

    internal EventSourcePage ReadEventSourceBound(string principalId, ReadEventSourceRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return Store.Read(view => ReadEventSource(view, principalId, request, budget));
    }

    internal AggregateReplayPage ReadAggregateReplayBound(string principalId, ReadAggregateReplayRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return Store.Read(view => AggregateReplayReader.Read(this, view, principalId, request, budget));
    }
}
