using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobCompleteCommitCutHealthyState(BlobRef Blob, Guid Upload, byte[] Bytes,
    BlobMetadata Metadata, BlobState State, ReplicatedOperation[] Operations, OperationResult[] Results);
