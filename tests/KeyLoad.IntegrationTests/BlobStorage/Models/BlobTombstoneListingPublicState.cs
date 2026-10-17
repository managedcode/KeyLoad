namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobTombstoneListingPublicState(AtomicPartitionPlacementResolution placement,
    BlobTombstoneListingPublicState.Retirement[] retired,
    BlobBoundedRetirementPublicPublication.Published first,
    BlobBoundedRetirementPublicPublication.Published current,
    BeginBlobUploadRequest begin, BlobCommitResult<BlobUploadInfo> begun)
{
    internal const string FirstRetiredId = "a-retired";
    internal const string SecondRetiredId = "b-retired";
    internal const string CurrentId = "c-current";
    internal const string UnpublishedId = "d-unpublished";
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal Retirement[] Retired { get; } = retired;
    internal BlobBoundedRetirementPublicPublication.Published First { get; } = first;
    internal BlobBoundedRetirementPublicPublication.Published Current { get; } = current;
    internal BeginBlobUploadRequest Begin { get; } = begin;
    internal BlobCommitResult<BlobUploadInfo> Begun { get; } = begun;
    internal BlobUploadInfo? Unpublished { get; set; } = begun.Value;
    internal AbortBlobUploadRequest Abort { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> Aborted { get; set; } = null!;
    internal ReclaimBlobRequest Reclaim { get; set; } = null!;
    internal BlobCommitResult<BlobReclaimResult> Reclaimed { get; set; } = null!;

    internal sealed record Retirement(BlobBoundedRetirementPublicPublication.Published Original,
        DeleteBlobRequest Delete, BlobCommitResult<BlobMetadata> Deleted, BlobMetadata Tombstone,
        ReclaimBlobRequest Reclaim, BlobCommitResult<BlobReclaimResult> Reclaimed);
}
