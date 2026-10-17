using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RecurringScheduleInspection? InspectRecurringScheduleBound(string principalId, InspectRecurringScheduleRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return InspectRecurringScheduleRead(principalId, request.Lane, request.ScheduleId, budget);
    }

    internal SagaInspection? InspectSagaBound(string principalId, InspectSagaRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return InspectSagaRead(principalId, request.Lane, request.SagaId, budget);
    }

    internal QueueTransferInspection? InspectQueueTransferBound(string principalId, InspectQueueTransferRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return InspectQueueTransferRead(principalId, request.SourceQueue, request.TransferId, budget);
    }

    internal QueueTransferReceiptInspection? InspectQueueTransferReceiptBound(string principalId, InspectQueueTransferReceiptRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return InspectQueueTransferReceiptRead(principalId, request.DestinationQueue, request.SourceQueue, request.TransferId, budget);
    }

    internal MessageInspection? InspectMessageBound(string principalId, InspectMessageRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return Store.Read(raw =>
        {
            var result = InspectMessageRead(budget.CreateView(raw), principalId, request.Lane, request.Id);
            budget.CheckResult(result);
            return result;
        });
    }
}
