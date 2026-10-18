using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.RecoveryTests.Features.BlobStorage;

internal sealed record BlobCompleteCommitCutHealthyState(BlobRef Blob, Guid Upload, byte[] Bytes,
    BlobMetadata Metadata, BlobState State, Command<DatabaseCommand>[] Operations, Result<object>[] Results);
