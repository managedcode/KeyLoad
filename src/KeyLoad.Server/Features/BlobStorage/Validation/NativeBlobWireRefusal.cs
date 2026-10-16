using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class NativeBlobWireRefusal
{
    private const int EmptySignatureLength = 0;

    internal static void Require(RemoteModelNativeRequest original, NativeControlledBlobFaultResult result)
    {
        var request = result.Request;
        var reply = result.Reply;
        if (request.Signature != original.Signature || request.Nonce == Guid.Empty || request.Nonce == original.Nonce
            || request.Channel != original.Channel || request.ExpiresAt != original.ExpiresAt
            || request.CallerVoter != original.CallerVoter || request.CallerSiloAddress != original.CallerSiloAddress
            || request.DestinationVoter != original.DestinationVoter
            || request.DestinationSiloAddress != original.DestinationSiloAddress || reply.Nonce != request.Nonce
            || !Exact(request.SourceOwner, original.SourceOwner) || !Exact(request.DestinationOwner, original.DestinationOwner))
        {
            throw Invalid();
        }
        if (reply.Error is not null)
        {
            if (reply.Error != ErrorCode.Unauthenticated || reply.SafeDetail != RemoteModelNativeProtocol.Unavailable
                || !reply.Payload.IsEmpty || reply.Signature is not { Length: EmptySignatureLength })
            {
                throw Invalid();
            }
            return;
        }
        // Both selected lifetime routes reject a changed full payload with its original inner MAC
        // before entering a typed receiver or beginning the Outcome effect boundary.
        throw Invalid();
    }

    private static bool Exact<T>(T first, T second)
        => NativeSerialization.Serialize(first).AsSpan().SequenceEqual(NativeSerialization.Serialize(second));
    private static InvalidOperationException Invalid() => new(NativeBlobWireProbeProtocol.InvalidEvidence);
}
