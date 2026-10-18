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
}
