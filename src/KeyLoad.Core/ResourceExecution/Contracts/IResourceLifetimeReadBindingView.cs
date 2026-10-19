namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Receives the actual resource pair only after authorization and the execution retirement check in the same read cut.</summary>
internal interface IResourceLifetimeReadBindingView
{
    void BindResourceLifetime(PartitionRef partition, ResourceDefinition definition, ResourceLifetime lifetime);
}
