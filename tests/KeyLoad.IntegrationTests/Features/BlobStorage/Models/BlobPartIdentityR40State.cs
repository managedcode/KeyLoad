namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobPartIdentityR40State(BeginBlobUploadRequest begin,
    AtomicPartitionPlacementResolution placement, OutboxStatus outbox)
{
    internal BeginBlobUploadRequest Begin { get; } = begin;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal OutboxStatus Outbox { get; } = outbox;
    internal BlobCommitResult<BlobUploadInfo> Begun { get; set; } = null!;
    internal WriteBlobPartRequest First { get; set; } = null!;
    internal WriteBlobPartRequest Changed { get; set; } = null!;
    internal BlobCommitResult<BlobUploadInfo> WrittenFirst { get; set; } = null!;
    internal WriteBlobPartRequest? Tail { get; set; }
    internal BlobCommitResult<BlobUploadInfo>? WrittenTail { get; set; }
    internal CompleteBlobUploadRequest? Complete { get; set; }
    internal BlobCommitResult<BlobMetadata>? Completed { get; set; }
    internal BlobUploadInfo Current => (WrittenTail ?? WrittenFirst).Value with
    { Status = Completed is null ? BlobUploadStatus.Active : BlobUploadStatus.Complete };
}
