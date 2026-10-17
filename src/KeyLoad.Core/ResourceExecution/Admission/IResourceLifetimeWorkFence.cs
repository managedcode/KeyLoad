namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Closes matching publication admission after the exact durable local owner fence has committed.</summary>
internal interface IResourceLifetimeWorkFence
{
    IResourceLifetimeWorkSettlement CloseMatching(ResourceRetirementOwnerFenceV1 ownerFence);
}
