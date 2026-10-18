using KeyLoad.Core.Features.TimeSeries;
using Orleans.CodeGeneration;

namespace KeyLoad.Clustering;

/// <summary>Accepts hints for already persisted work; a RAM enqueue is not a durability acknowledgement.</summary>
[Version(BackgroundWorkerIdentity.InterfaceVersion)]
internal interface ISampleChunkWorkerGrain : IGrainWithStringKey
{
    Task WakeAsync(CancellationToken cancellationToken);
    Task EnqueueAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken);
}
