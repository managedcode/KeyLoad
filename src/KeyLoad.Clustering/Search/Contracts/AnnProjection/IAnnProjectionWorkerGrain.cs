using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Accepts hints for already persisted work; a RAM enqueue is not a durability acknowledgement.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
internal interface IAnnProjectionWorkerGrain : IGrainWithStringKey
{
    Task WakeAsync(CancellationToken cancellationToken);
}
