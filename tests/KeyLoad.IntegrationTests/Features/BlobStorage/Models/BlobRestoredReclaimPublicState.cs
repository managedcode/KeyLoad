namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobRestoredReclaimPublicState(BlobArchiveActivePublicVersion published,
    BlobArchiveActivePublicVersion active, Guid owner)
{
    internal BlobArchiveActivePublicVersion Published { get; } = published;
    internal BlobArchiveActivePublicVersion Active { get; } = active;
    internal Guid Owner { get; } = owner;
    internal BlobArchiveActivePublicVersion? Healthy { get; set; }
    internal ReclaimBlobRequest First { get; } = new(Guid.NewGuid(), active.Begin.Blob, active.Begin.UploadId, 1);
    internal ReclaimBlobRequest Final { get; } = new(Guid.NewGuid(), active.Begin.Blob, active.Begin.UploadId, 1);
    internal BlobCommitResult<BlobReclaimResult>? FirstResult { get; set; }
    internal BlobCommitResult<BlobReclaimResult>? FinalResult { get; set; }
    internal BeginBlobUploadRequest Before { get; } = new(Guid.NewGuid(), active.Begin.Blob, Guid.NewGuid(), 18, 0);
    internal BeginBlobUploadRequest Over { get; } = new(Guid.NewGuid(), active.Begin.Blob, Guid.NewGuid(), 65554, 0);
}
