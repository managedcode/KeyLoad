using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.Orleans;

[global::Orleans.CodeGeneration.Version(SampleChunkJobProtocol.CoordinatorInterfaceVersion)]
internal interface ISampleChunkCoordinatorGrain : IGrainWithStringKey
{
    Task ScheduleAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken);
}
