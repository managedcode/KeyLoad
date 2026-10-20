using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes EventStreamsQueries operations against the caller's database context.</summary>
public static class EventStreamsQueries
{
    internal static StreamPage ReadStreamBound(this DatabaseEngine engine, string principalId, ReadStreamRequest request, PhysicalShardRecord configuredOwner, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(configuredOwner);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        engine.VerifyStorageOwner(configuredOwner, budget, budget.Cancellation);
        return engine.ReadStreamTraversal(principalId, request, budget);
    }

    internal static EventSourcePage ReadEventSourceBound(this DatabaseEngine engine, string principalId, ReadEventSourceRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(view => engine.ReadEventSource(view, principalId, request, budget));
    }

    internal static AggregateReplayPage ReadAggregateReplayBound(this DatabaseEngine engine, string principalId, ReadAggregateReplayRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(view => AggregateReplayReader.Read(engine, view, principalId, request, budget));
    }
}
