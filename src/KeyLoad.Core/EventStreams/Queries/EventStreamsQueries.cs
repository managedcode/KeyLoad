using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes EventStreamsQueries operations against the caller's database context.</summary>
public static class EventStreamsQueries
{
    internal static StreamPage ReadStreamBound(this DatabaseEngine engine, string principalId, ReadStreamRequest request, PhysicalShardRecord configuredOwner, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(configuredOwner) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        engine.VerifyStorageOwner(configuredOwner, budget, budget.Cancellation);
        return engine.ReadStreamTraversal(principalId, request, budget);
    }

    internal static EventSourcePage ReadEventSourceBound(this DatabaseEngine engine, string principalId, ReadEventSourceRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(view => engine.ReadEventSource(view, principalId, request, budget));
    }

    internal static AggregateReplayPage ReadAggregateReplayBound(this DatabaseEngine engine, string principalId, ReadAggregateReplayRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(view => AggregateReplayReader.Read(engine, view, principalId, request, budget));
    }
}
