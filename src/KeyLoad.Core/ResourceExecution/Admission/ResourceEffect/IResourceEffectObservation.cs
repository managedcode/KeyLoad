namespace KeyLoad.Core.Features.ResourceExecution;

internal interface IResourceEffectObservation
{
    void ObserveResource(PartitionRef partition, ResourceLifetimeV1 lifetime);
    void ObserveLifetime(ResourceLifetimeV1 lifetime);
}
