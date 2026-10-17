namespace KeyLoad.Clustering;

internal static class DueCoordinatorAliases
{
    internal const string Service = "keyload.orleans.messaging.due-service.v1";
    internal const string Coordinator = "keyload.orleans.messaging.due-coordinator.v1";
    internal const string Result = "keyload.orleans.messaging.due-dispatch-result.v1";
    internal const string Process = nameof(IRecurringDueCoordinatorGrain.ProcessDueAsync);
    internal const string ProcessQueue = nameof(IRecurringDueCoordinatorGrain.ProcessQueueDeadlineAsync);
    internal const string ProcessTransfer = nameof(IRecurringDueCoordinatorGrain.ProcessQueueTransferAsync);
    internal const int CoordinatorInterfaceVersion = 1;
}
