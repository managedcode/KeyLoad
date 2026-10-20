using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes MessagingQueries operations against the caller's database context.</summary>
public static class MessagingQueries
{
    internal static RecurringScheduleInspection? InspectRecurringScheduleBound(this DatabaseEngine engine, string principalId, InspectRecurringScheduleRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.InspectRecurringScheduleRead(principalId, request.Lane, request.ScheduleId, budget);
    }

    internal static SagaInspection? InspectSagaBound(this DatabaseEngine engine, string principalId, InspectSagaRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.InspectSagaRead(principalId, request.Lane, request.SagaId, budget);
    }

    internal static QueueTransferInspection? InspectQueueTransferBound(this DatabaseEngine engine, string principalId, InspectQueueTransferRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.InspectQueueTransferRead(principalId, request.SourceQueue, request.TransferId, budget);
    }

    internal static QueueTransferReceiptInspection? InspectQueueTransferReceiptBound(this DatabaseEngine engine, string principalId, InspectQueueTransferReceiptRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.InspectQueueTransferReceiptRead(principalId, request.DestinationQueue, request.SourceQueue, request.TransferId, budget);
    }

    internal static MessageInspection? InspectMessageBound(this DatabaseEngine engine, string principalId, InspectMessageRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        budget.Check();
        return engine.Store.Read(raw =>
        {
            var result = engine.InspectMessageRead(budget.CreateView(raw), principalId, request.Lane, request.Id);
            budget.CheckResult(result);
            return result;
        });
    }
}
