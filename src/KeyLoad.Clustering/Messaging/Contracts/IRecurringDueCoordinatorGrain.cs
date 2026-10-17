using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Clustering;

[global::Orleans.Alias(DueCoordinatorAliases.Coordinator),
 global::Orleans.CodeGeneration.Version(DueCoordinatorAliases.CoordinatorInterfaceVersion)]
internal interface IRecurringDueCoordinatorGrain : global::Orleans.IGrainWithStringKey
{
    [global::Orleans.Alias(DueCoordinatorAliases.Process)]
    Task<DueDispatchResult> ProcessDueAsync(DueWorkHint hint, CancellationToken cancellationToken);

    [global::Orleans.Alias(DueCoordinatorAliases.ProcessQueue)]
    Task<DueDispatchResult> ProcessQueueDeadlineAsync(QueueDeadlineHint hint, CancellationToken cancellationToken);

    [global::Orleans.Alias(DueCoordinatorAliases.ProcessTransfer)]
    Task<DueDispatchResult> ProcessQueueTransferAsync(RemoteTransferCoordinationHint hint, CancellationToken cancellationToken);
}
