using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireIdentityRules
{
    internal static NativeBlobWireIdentity Identity(ControlledBlobReadPurpose purpose, OperationKind? kind)
        => (purpose, kind) switch
        {
            (ControlledBlobReadPurpose.Outcome, OperationKind.BeginBlobUpload) => NativeBlobWireIdentity.BeginUpload,
            (ControlledBlobReadPurpose.Outcome, OperationKind.WriteBlobPart) => NativeBlobWireIdentity.WritePart,
            (ControlledBlobReadPurpose.Outcome, OperationKind.CompleteBlobUpload) => NativeBlobWireIdentity.CompleteUpload,
            (ControlledBlobReadPurpose.Outcome, OperationKind.AbortBlobUpload) => NativeBlobWireIdentity.AbortUpload,
            (ControlledBlobReadPurpose.Outcome, OperationKind.DeleteBlob) => NativeBlobWireIdentity.Delete,
            (ControlledBlobReadPurpose.Outcome, OperationKind.ReclaimBlob) => NativeBlobWireIdentity.Reclaim,
            (ControlledBlobReadPurpose.Metadata, null) => NativeBlobWireIdentity.Metadata,
            (ControlledBlobReadPurpose.UploadInfo, null) => NativeBlobWireIdentity.UploadInfo,
            (ControlledBlobReadPurpose.Range, null) => NativeBlobWireIdentity.Range,
            (ControlledBlobReadPurpose.List, null) => NativeBlobWireIdentity.List,
            _ => throw new InvalidOperationException(NativeBlobWireProbeProtocol.InvalidEvidence)
        };

    internal static void Require(NativeBlobWireArm arm)
    {
        if (arm.ArmId == Guid.Empty || !Enum.IsDefined(arm.Identity)
            || Identity(arm.Purpose, arm.OperationKind) != arm.Identity || string.IsNullOrEmpty(arm.BlobId))
        {
            throw Invalid();
        }
        var outcome = arm.Purpose == ControlledBlobReadPurpose.Outcome;
        if (outcome != arm.OriginalCommandId.HasValue || arm.OriginalCommandId == Guid.Empty
            || outcome && arm.NativeRequestDigest is not null
            || !outcome && !NativeBlobWireShape.IsDigest(arm.NativeRequestDigest))
        {
            throw Invalid();
        }
        if (arm.Identity == NativeBlobWireIdentity.Delete && arm.UploadId is not null
            || outcome && arm.Identity != NativeBlobWireIdentity.Delete && !arm.UploadId.HasValue
            || arm.UploadId == Guid.Empty)
        {
            throw Invalid();
        }
    }

    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
