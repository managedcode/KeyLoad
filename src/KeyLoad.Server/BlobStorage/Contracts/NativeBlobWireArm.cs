using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed record NativeBlobWireArm(NativeBlobWireIdentity Identity, Guid ArmId,
    ControlledBlobReadPurpose Purpose, OperationKind? OperationKind, Guid? OriginalCommandId,
    string BlobId, Guid? UploadId, string? NativeRequestDigest);
