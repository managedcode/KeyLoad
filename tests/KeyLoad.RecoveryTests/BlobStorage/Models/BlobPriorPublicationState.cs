using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobPriorPublicationState(BlobRef Blob, Guid Upload, byte[] Bytes, BlobMetadata Metadata,
    BlobState State, BlobPriorPublicationCommand[] Commands, byte[] Identity, byte[] Principal);
