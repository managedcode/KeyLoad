using KeyLoad.Core.Messaging;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCoordinationBoundReads operations against the caller's database context.</summary>
public static class MessagingCoordinationBoundReads
{
    internal static SubscriptionInfo GetSubscriptionBound(this DatabaseEngine engine, string principalId, GetSubscriptionRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(raw =>
        {
            var result = engine.GetSubscriptionRead(budget.CreateView(raw), principalId, request.Subscription);
            return result;
        });
    }

    internal static RemoteTransferCoordinationReadResult ReadRemoteTransferCoordinationBound(this DatabaseEngine engine, string subject, RemoteTransferCoordinationReadRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        RemoteTransferAttemptReadShape.Require(request);
        var pending = RemoteTransferPendingBudget.Coordination(engine, budget);
        var result = engine.Store.Read(view => engine.ReadRemoteTransferCoordination(pending.View(view), subject, request));
        return result;
    }
}
