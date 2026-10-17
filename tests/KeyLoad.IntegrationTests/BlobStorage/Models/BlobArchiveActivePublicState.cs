
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal sealed class BlobArchiveActivePublicState(BlobArchiveActivePublicVersion published,
    BlobArchiveActivePublicVersion active, PrincipalRecord reader, string key, Guid sourceOwner)
{
    internal BlobArchiveActivePublicVersion Published { get; set; } = published;
    internal BlobArchiveActivePublicVersion OriginalPublished { get; } = published;
    internal BlobArchiveActivePublicVersion Active { get; } = active;
    internal PrincipalRecord Reader { get; set; } = reader;
    internal string Key { get; } = key;
    internal Guid SourceOwner { get; } = sourceOwner;
    internal WriteBlobPartRequest RefusedPart
    {
        get;
    } = new(Guid.NewGuid(), active.Begin.Blob, active.Begin.UploadId,
        2, BlobArchiveActivePublicPublication.Bytes(2, 17), BlobIntegrity.PartHash(BlobArchiveActivePublicPublication.Bytes(2, 17)));
}

internal sealed record BlobArchiveActivePublicVersion(BeginBlobUploadRequest Begin,
    BlobCommitResult<BlobUploadInfo> Begun, WriteBlobPartRequest[] Parts,
    BlobCommitResult<BlobUploadInfo>[] Written, CompleteBlobUploadRequest? Complete,
    BlobCommitResult<BlobMetadata>? Completed);
