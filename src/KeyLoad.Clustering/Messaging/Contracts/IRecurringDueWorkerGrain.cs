using KeyLoad.Core.Features.Messaging;
using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Accepts hints for already persisted work; a RAM enqueue is not a durability acknowledgement.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
internal interface IRecurringDueWorkerGrain : IGrainWithStringKey
{
    Task WakeAsync(CancellationToken cancellationToken);
    Task EnqueueAsync(DueWorkHint hint, CancellationToken cancellationToken);
}
