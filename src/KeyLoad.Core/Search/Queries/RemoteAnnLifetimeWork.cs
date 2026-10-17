using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteAnnReadFenceV2 CaptureRemoteAnnLifetimeReadBound(string principalId, ProjectionConsumerRef consumer,
        string collection, string field, VectorSpace space, long generation,
        RegisteredPhysicalOwnerV1 configuredLocal, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        budget.AttachResourceLifetimeBinding(binding);
        return CaptureRemoteAnnLifetimeRead(principalId, consumer, collection, field, space, generation,
            configuredLocal, budget);
    }

    internal void ValidateRemoteAnnLifetimeReadBound(RemoteAnnReadFenceV2 original, ProjectionConsumerRef consumer,
        string collection, string field, VectorSpace space, long generation, ReadExecutionBudget budget,
        IResourceLifetimeWorkBinding binding)
    {
        budget.RequireResourceLifetimeBinding(binding);
        ValidateRemoteAnnLifetimeRead(original, consumer, collection, field, space, generation, budget);
    }
}
