using KeyLoad.Core.Features.BlobStorage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobCompleteCommitCutHealthyState(BlobRef Blob, Guid Upload, byte[] Bytes,
    BlobMetadata Metadata, BlobState State, Command<DatabaseCommand>[] Operations, Result<object>[] Results);
