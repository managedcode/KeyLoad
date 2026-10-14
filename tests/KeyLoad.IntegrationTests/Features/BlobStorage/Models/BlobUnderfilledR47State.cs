namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobUnderfilledR47State(BlobPartIdentityR40State original)
{
    internal BlobPartIdentityR40State Original { get; } = original;
    internal BlobTailShapeR48State TailShape { get; } = new(original);
    internal CompleteBlobUploadRequest Premature { get; } = new(Guid.NewGuid(), original.Begin.Blob,
        original.Begin.UploadId, BlobPartIdentityR40Corpus.Next(original.WrittenFirst.Value.IntegrityHash,
            1, 4, BlobPartIdentityR40Corpus.TailSha));
}
