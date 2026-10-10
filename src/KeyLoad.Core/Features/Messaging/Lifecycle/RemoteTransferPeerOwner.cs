using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.Messaging;

internal sealed class RemoteTransferPeerOwner(
    RegisteredPhysicalOwnerV1 source, RegisteredPhysicalOwnerV1 destination, string technicalPrincipal,
    Func<ReadOnlyMemory<byte>, string, RemoteQueueTransferPeerCall> verify)
{
    private int closed;
    internal string TechnicalPrincipal { get; } = technicalPrincipal;

    internal void Require(RemoteTransferNativeProof proof)
    {
        if (Volatile.Read(ref closed) != RemoteTransferPeerProtocol.EmptyEncodedBytes
            || string.IsNullOrWhiteSpace(TechnicalPrincipal) || proof.TechnicalPrincipalId != TechnicalPrincipal)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteTransferPeerProtocol.Unavailable); }
        var decoded = verify(proof.OriginalEnvelope, proof.OriginalSignature);
        if (!PhysicalOwnerEntryValidation.Same(source, decoded.SourceOwner)
            || !PhysicalOwnerEntryValidation.Same(destination, decoded.DestinationOwner)
            || !NativeSerialization.Serialize(decoded).AsSpan().SequenceEqual(NativeSerialization.Serialize(proof.Call)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteTransferPeerProtocol.Invalid); }
    }

    internal void Close() => Interlocked.Exchange(ref closed, RemoteTransferPeerProtocol.Version);
}
