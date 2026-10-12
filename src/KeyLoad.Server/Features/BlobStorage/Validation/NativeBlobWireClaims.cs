using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class NativeBlobWireClaims(IOptions<OrleansMembershipOptions> membership,
    NativeBlobWireShape shape)
{
    private const string CompactNonceFormat = "N";

    internal static NativeBlobWireClaim Create(NativeBlobWireOwner owner, string ownerDigest,
        string armsDigest, NativeBlobWireArm arm, RemoteControlledBlobCall call)
    {
        var frame = call.Request.Frame;
        return new(NativeBlobWireProbeProtocol.Version, owner.SessionId, ownerDigest, armsDigest,
            arm.ArmId, arm.Identity, call.CallerVoter, call.CallerSiloAddress, call.RequestId,
            frame.QueryId, call.Nonce, frame.ExpiresAt, Digest(call));
    }

    internal void Require(NativeBlobWireOwner owner, string ownerDigest, string armsDigest,
        NativeBlobWireArm arm, NativeBlobWireClaim claim)
    {
        if (claim.Version != NativeBlobWireProbeProtocol.Version || claim.SessionId != owner.SessionId
            || claim.OwnerDigest != ownerDigest || claim.ArmsDigest != armsDigest
            || claim.ArmId != arm.ArmId || claim.Identity != arm.Identity || claim.RequestId == Guid.Empty
            || claim.QueryId == Guid.Empty || !owner.SourceOwner.VoterIds.Contains(claim.CallerVoter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(claim.CallerSiloAddress, membership)
            || !Guid.TryParseExact(claim.Nonce, CompactNonceFormat, out var nonce) || nonce == Guid.Empty
            || claim.ExpiresAt.Offset != TimeSpan.Zero || !NativeBlobWireShape.IsDigest(claim.OriginalCallDigest))
        { throw Invalid(); }
        shape.Text(claim.CallerVoter);
    }

    internal static string Digest(RemoteControlledBlobCall call)
    {
        if (NativeSerialization.Measure(call) > RequestCqrsProbeProtocol.MaximumRecordBytes)
        { throw Invalid(); }
        return NativeBlobWireCodec.Digest(NativeSerialization.Serialize(call));
    }

    internal static void RequireOwn(NativeBlobWireClaim claim, RemoteControlledBlobCall call)
    {
        if (claim.OriginalCallDigest != Digest(call) || claim.CallerVoter != call.CallerVoter
            || claim.CallerSiloAddress != call.CallerSiloAddress || claim.RequestId != call.RequestId
            || claim.QueryId != call.Request.Frame.QueryId || claim.Nonce != call.Nonce
            || claim.ExpiresAt != call.Request.Frame.ExpiresAt)
        { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
