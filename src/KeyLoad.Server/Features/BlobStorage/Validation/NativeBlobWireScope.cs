using KeyLoad.Core.Features.BlobStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireScope
{
    internal static NativeBlobWireArm? Match(NativeBlobWireOwner owner, NativeBlobWireArms arms,
        RemoteControlledBlobCall call)
    {
        var frame = call.Request.Frame;
        if (frame.Principal.Id != arms.PrincipalId || frame.Principal.TenantId != arms.TenantId
            || frame.Principal.PolicyEpoch != arms.PolicyEpoch || frame.Control.Partition != arms.Partition
            || frame.Control.MoveId != arms.MoveId || frame.Resource.Name != arms.ResourceName
            || frame.Resource.Kind != arms.ResourceKind || frame.Resource.TransactionDomainId != arms.TransactionDomainId)
        { return null; }
        if (!NativeBlobWireShape.Same(owner.SourceOwner, call.Source)
            || !NativeBlobWireShape.Same(owner.DestinationOwner, call.Destination.Owner)
            || !owner.SourceOwner.VoterIds.Contains(call.CallerVoter, StringComparer.Ordinal))
        { throw Invalid(); }
        var identity = NativeBlobWireIdentityRules.Identity(frame.Purpose, frame.Original?.Kind);
        var arm = arms.Arms[(int)identity];
        var blob = BlobControlledReadScope.Require(frame);
        if (blob.Partition != arms.Partition || blob.Resource != arms.ResourceName || blob.Id != arm.BlobId)
        { return null; }
        if (frame.Original is { } operation)
        {
            var scope = BlobCommandScope.From(operation);
            if (operation.Id != arm.OriginalCommandId || scope.UploadId != arm.UploadId) { return null; }
        }
        else if (NativeBlobWireCodec.Digest(frame.NativeRequest.Span) != arm.NativeRequestDigest)
        { return null; }
        return arm;
    }
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
