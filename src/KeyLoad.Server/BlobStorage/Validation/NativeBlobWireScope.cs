using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireScope
{
    internal static NativeBlobWireArm? Match(NativeBlobWireOwner owner, NativeBlobWireArms arms,
        RemoteControlledBlobOutcomeCallV2 original)
    {
        var call = original.Original;
        if (call.Request.Frame.Purpose != ControlledBlobReadPurpose.Outcome)
        {
            throw Invalid();
        }
        return MatchFrame(owner, arms, call.Source, call.Destination.Owner, call.CallerVoter, call.Request.Frame);
    }

    internal static NativeBlobWireArm? Match(NativeBlobWireOwner owner, NativeBlobWireArms arms,
        RemoteResourceLifetimeReadCallV2 call)
    {
        var request = NativeControlledBlobLifetimeCall.Require(call, RemoteDocumentProtocol.MaximumBodyBytes);
        return MatchFrame(owner, arms, call.Source, call.Destination.Owner, call.CallerVoter, request.Frame.Original);
    }

    private static NativeBlobWireArm? MatchFrame(NativeBlobWireOwner owner, NativeBlobWireArms arms,
        PhysicalShardRecord source, PhysicalShardRecord destination, string callerVoter, ControlledBlobReadFrame frame)
    {
        if (frame.Principal.Id != arms.PrincipalId || frame.Principal.TenantId != arms.TenantId
            || frame.Principal.PolicyEpoch != arms.PolicyEpoch || frame.Control.Partition != arms.Partition
            || frame.Control.MoveId != arms.MoveId || frame.Resource.Name != arms.ResourceName
            || frame.Resource.Kind != arms.ResourceKind || frame.Resource.TransactionDomainId != arms.TransactionDomainId)
        {
            return null;
        }
        if (!NativeBlobWireShape.Same(owner.SourceOwner, source)
            || !NativeBlobWireShape.Same(owner.DestinationOwner, destination)
            || !owner.SourceOwner.VoterIds.Contains(callerVoter, StringComparer.Ordinal))
        {
            throw Invalid();
        }
        var identity = NativeBlobWireIdentityRules.Identity(frame.Purpose, frame.Original?.Kind);
        var arm = arms.Arms[(int)identity];
        var blob = BlobControlledReadScope.Require(frame);
        if (blob.Partition != arms.Partition || blob.Resource != arms.ResourceName || blob.Id != arm.BlobId)
        {
            return null;
        }
        if (frame.Original is { } operation)
        {
            var scope = BlobCommandScope.From(operation);
            if (operation.Id != arm.OriginalCommandId || scope.UploadId != arm.UploadId)
            {
                return null;
            }
        }
        else if (NativeBlobWireCodec.Digest(frame.NativeRequest.Span) != arm.NativeRequestDigest)
        {
            return null;
        }
        return arm;
    }
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
