using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobMaintenancePolicyPublicState(BlobRef blob, McpPersistedIdentity identity,
    BlobBoundedRetirementPublicPublication.Published original, AtomicPartitionPlacementResolution placement)
{
    internal BlobRef Blob { get; } = blob;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal McpPersistedIdentity Identity { get; } = identity;
    internal BlobBoundedRetirementPublicPublication.Published Original { get; } = original;
    internal DeleteBlobRequest DeniedDelete { get; } = new(Guid.NewGuid(), blob, 1L);
    internal DeleteBlobRequest Delete { get; set; } = null!;
    internal BlobCommitResult<BlobMetadata> Deleted { get; set; } = null!;
    internal BlobMetadata Tombstone { get; set; } = null!;
    internal ReclaimBlobRequest DeniedReclaim { get; } = new(Guid.NewGuid(), blob, original.Begin.UploadId, 1);
    internal ReclaimBlobRequest Reclaim { get; set; } = null!;
    internal BlobCommitResult<BlobReclaimResult> Reclaimed { get; set; } = null!;
    internal BlobBoundedRetirementPublicPublication.Published Healthy { get; set; } = null!;
}
