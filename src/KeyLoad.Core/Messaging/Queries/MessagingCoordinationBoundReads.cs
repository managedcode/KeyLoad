using KeyLoad.Core.Messaging;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCoordinationBoundReads operations against the caller's database context.</summary>
public static class MessagingCoordinationBoundReads
{
    internal static SubscriptionInfo GetSubscriptionBound(this DatabaseEngine engine, string principalId, GetSubscriptionRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(raw =>
        {
            var result = engine.GetSubscriptionRead(budget.CreateView(raw), principalId, request.Subscription);
            budget.CheckResult(result);
            return result;
        });
    }

    internal static RemoteTransferCoordinationReadResult ReadRemoteTransferCoordinationBound(this DatabaseEngine engine, string subject, RemoteTransferCoordinationReadRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        RemoteTransferAttemptReadShape.Require(request);
        using var pending = RemoteTransferPendingBudget.Coordination(engine, budget);
        var result = engine.Store.Read(view => engine.ReadRemoteTransferCoordination(pending.View(view), subject, request));
        pending.Result(result);
        return result;
    }
}
