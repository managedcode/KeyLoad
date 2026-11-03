namespace KeyLoad.Clustering;

internal interface IAnnProjectionCoordinatorGrain : global::Orleans.IGrainWithStringKey
{
    Task ScheduleAsync(ProjectionConsumerRef consumer, long generation, Guid nodeId, CancellationToken cancellationToken);
}
