using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core.Features.BlobStorage;

public sealed partial class BlobStorageOperations
{
    internal BlobMetadata? MetadataBound(string principalId, BlobMetadataRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
        => Reads.CutBound(principalId, (view, principal) => Reads.Metadata(view, principal, request), budget, binding);

    internal BlobUploadInfo? UploadInfoBound(string principalId, BlobUploadInfoRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
        => Reads.CutBound(principalId, (view, principal) => Reads.UploadInfo(view, principal, request), budget, binding);

    internal BlobReadResult ReadBound(string principalId, BlobReadRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
        => Reads.CutBound(principalId, (view, principal) => Reads.Range(view, principal, request), budget, binding);

    internal BlobListPage ListBound(string principalId, BlobListRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
        => Reads.CutBound(principalId, (view, principal) => Reads.List(view, principal, request), budget, binding);
}
