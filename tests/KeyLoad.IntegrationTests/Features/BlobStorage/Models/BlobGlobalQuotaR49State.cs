namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobGlobalQuotaR49State(PartitionRef partition, AtomicPartitionPlacementResolution placement, OutboxStatus outbox)
{
    internal PartitionRef Partition { get; } = partition;
    internal AtomicPartitionPlacementResolution Placement { get; } = placement;
    internal OutboxStatus Outbox { get; } = outbox;
    internal HashSet<string> ConfiguredResources { get; } = [];
    internal BlobGlobalQuotaR49Upload Control { get; } = new(new(partition, BlobGlobalQuotaR49Corpus.ControlResource, BlobGlobalQuotaR49Corpus.ControlName), BlobGlobalQuotaR49Corpus.SmallLength);
    internal BlobGlobalQuotaR49Upload Held { get; } = new(new(partition, BlobGlobalQuotaR49Corpus.FirstResource, BlobGlobalQuotaR49Corpus.HeldName), BlobGlobalQuotaR49Corpus.FirstReservation);
    internal BlobGlobalQuotaR49Upload Attempt { get; } = new(new(partition, BlobGlobalQuotaR49Corpus.SecondResource, BlobGlobalQuotaR49Corpus.TargetName), BlobGlobalQuotaR49Corpus.SecondReservation);
    internal BlobGlobalQuotaR49Upload? Healthy { get; set; }
    internal BlobGlobalQuotaR49Upload[] CurrentUploads => Healthy is { } healthy ? [Control, Held, Attempt, healthy] : [Control, Held, Attempt];
}
internal sealed class BlobGlobalQuotaR49Upload(BlobRef blob, long length)
{
    internal BlobRef Blob { get; } = blob;
    internal BeginBlobUploadRequest Begin { get; } = new(Guid.NewGuid(), blob, Guid.NewGuid(), length, 0L);
    internal BeginBlobUploadRequest? AcceptedBegin { get; set; }
    internal BlobCommitResult<BlobUploadInfo>? Begun { get; set; }
    internal List<WriteBlobPartRequest> Parts { get; } = [];
    internal List<BlobCommitResult<BlobUploadInfo>> Written { get; } = [];
    internal CompleteBlobUploadRequest? Complete { get; set; }
    internal BlobCommitResult<BlobMetadata>? Completed { get; set; }
    internal AbortBlobUploadRequest? Abort { get; set; }
    internal BlobCommitResult<BlobUploadInfo>? Aborted { get; set; }
    internal ReclaimBlobRequest? Reclaim { get; set; }
    internal BlobCommitResult<BlobReclaimResult>? Reclaimed { get; set; }
    internal BlobUploadInfo? Progress { get; set; }
    internal BlobMetadata? Metadata { get; set; }
    internal bool Removed { get; set; }
}
