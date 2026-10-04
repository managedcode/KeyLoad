using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Orleans;

/// <summary>Dispatches bounded blob reads using borrowed node-local engine ownership.</summary>
internal sealed class GrainBlobReadCapabilities(DatabaseEngine database)
{
    private readonly BlobStorageOperations blobs = new(database);

    internal static bool Handles(GrainReadKind kind) => kind is GrainReadKind.BlobMetadata
        or GrainReadKind.BlobUploadInfo or GrainReadKind.BlobRange or GrainReadKind.BlobList;

    internal object? Execute(GrainReadKind kind, string principal, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.BlobMetadata => blobs.Metadata(principal, GrainNativePayload.Read<BlobMetadataRequest>(payload), cancellationToken),
            GrainReadKind.BlobUploadInfo => blobs.UploadInfo(principal, GrainNativePayload.Read<BlobUploadInfoRequest>(payload), cancellationToken),
            GrainReadKind.BlobRange => blobs.Read(principal, GrainNativePayload.Read<BlobReadRequest>(payload), cancellationToken),
            GrainReadKind.BlobList => blobs.List(principal, GrainNativePayload.Read<BlobListRequest>(payload), cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
