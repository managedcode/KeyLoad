using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobPriorPublicationCommand(ReplicatedOperation Operation, OperationResult Result);

internal sealed record BlobPriorPublicationState(BlobRef Blob, Guid Upload, byte[] Bytes, BlobMetadata Metadata,
    BlobState State, BlobPriorPublicationCommand[] Commands, byte[] Identity, byte[] Catalog, byte[] Principal);

internal sealed record BlobPriorPublicationFault(ReplicatedOperation Delete, ReplicatedOperation Reclaim,
    ReplicatedOperation QueueOperation, string[] OriginalImage, string[] DamagedImage, long Position,
    byte[] Key, byte[] OriginalRow);
