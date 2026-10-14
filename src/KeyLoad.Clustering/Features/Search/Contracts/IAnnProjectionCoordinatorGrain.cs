namespace KeyLoad.Clustering;

[global::Orleans.CodeGeneration.Version(AnnProjectionJobProtocol.InterfaceVersion)]
internal interface IAnnProjectionCoordinatorGrain : global::Orleans.IGrainWithStringKey
{
    Task ScheduleAsync(ProjectionConsumerRef consumer, long generation, Guid nodeId, CancellationToken cancellationToken);
}
