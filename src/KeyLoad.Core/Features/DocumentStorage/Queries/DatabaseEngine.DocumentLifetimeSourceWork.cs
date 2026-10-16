using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteDocumentReadFrameV2? CaptureRemoteDocumentReadV2Bound(string principalId, EntityRef reference,
        DateTimeOffset expiry, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        work.AttachResourceLifetimeBinding(binding);
        return CaptureRemoteDocumentReadV2(principalId, reference, expiry, work);
    }

    internal void ValidateRemoteDocumentReadV2Bound(RemoteDocumentReadFrameV2 original,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        work.RequireResourceLifetimeBinding(binding);
        ValidateRemoteDocumentReadV2(original, work);
    }

    internal PartitionControlDocumentReadFrameV2? TryCaptureControlledDocumentReadV2Bound(string principalId,
        EntityRef reference, CommitToken? minimumToken, DateTimeOffset expiry, ReadExecutionBudget work,
        IResourceLifetimeWorkBinding binding)
    {
        work.AttachResourceLifetimeBinding(binding);
        return TryCaptureControlledDocumentReadV2(principalId, reference, minimumToken, expiry, work);
    }

    internal void ValidateControlledDocumentReadV2Bound(PartitionControlDocumentReadFrameV2 original,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        work.RequireResourceLifetimeBinding(binding);
        ValidateControlledDocumentReadV2(original, work);
    }
}
