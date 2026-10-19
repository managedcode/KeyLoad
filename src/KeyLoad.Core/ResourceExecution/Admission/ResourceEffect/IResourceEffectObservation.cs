namespace KeyLoad.Core.Features.ResourceExecution;

internal interface IResourceEffectObservation
{
    void ObserveResource(PartitionRef partition, ResourceLifetime lifetime);
    void ObserveLifetime(ResourceLifetime lifetime);
}
