using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal SubscriptionInfo GetSubscriptionBound(string principalId, GetSubscriptionRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return Store.Read(raw =>
        {
            var result = GetSubscriptionRead(budget.CreateView(raw), principalId, request.Subscription);
            budget.CheckResult(result);
            return result;
        });
    }

    internal RemoteTransferCoordinationReadResult ReadRemoteTransferCoordinationBound(string subject,
        RemoteTransferCoordinationReadRequest request, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        RemoteTransferAttemptReadShape.Require(request);
        using var pending = RemoteTransferPendingBudget.Coordination(this, budget);
        var result = Store.Read(view => ReadRemoteTransferCoordination(pending.View(view), subject, request));
        pending.Result(result);
        return result;
    }
}
