using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes MessagingQueries operations against the caller's database context.</summary>
public static class MessagingQueries
{
    internal static RecurringScheduleInspection? InspectRecurringScheduleBound(this DatabaseEngine engine, string principalId, InspectRecurringScheduleRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        return engine.InspectRecurringScheduleRead(principalId, request.Lane, request.ScheduleId, budget);
    }

    internal static SagaInspection? InspectSagaBound(this DatabaseEngine engine, string principalId, InspectSagaRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        return engine.InspectSagaRead(principalId, request.Lane, request.SagaId, budget);
    }

    internal static QueueTransferInspection? InspectQueueTransferBound(this DatabaseEngine engine, string principalId, InspectQueueTransferRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        return engine.InspectQueueTransferRead(principalId, request.SourceQueue, request.TransferId, budget);
    }

    internal static QueueTransferReceiptInspection? InspectQueueTransferReceiptBound(this DatabaseEngine engine, string principalId, InspectQueueTransferReceiptRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        return engine.InspectQueueTransferReceiptRead(principalId, request.DestinationQueue, request.SourceQueue, request.TransferId, budget);
    }

    internal static MessageInspection? InspectMessageBound(this DatabaseEngine engine, string principalId, InspectMessageRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(raw =>
        {
            var result = (engine.InspectMessageRead(budget.CreateView(raw), principalId, request.Lane, request.Id)).Value;
            return result;
        });
    }
}
