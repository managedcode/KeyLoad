using KeyLoad.Core.TimeSeries;

namespace KeyLoad.Clustering;

internal interface ISampleChunkCoordinatorGrain : IGrainWithStringKey
{
    Task ScheduleAsync(SampleChunkWorkHint hint, CancellationToken cancellationToken);
}
