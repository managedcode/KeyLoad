using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteModelNativeIdentity
{
    private const int NativeNonceCharacters = 32;
    private const string NonceFormat = "N";
    private const int RequiredReplicaVoters = 3;
    private const int EmptySignatureCharacters = 0;

    internal static bool ValidNonce(string? value)
        => value is { Length: NativeNonceCharacters } && Guid.TryParseExact(value, NonceFormat, out var nonce) && nonce != Guid.Empty
            && string.Equals(nonce.ToString(NonceFormat), value, StringComparison.Ordinal);

    internal static void RequireOwner(NodeOptions options, PartitionHost partition, PhysicalShardRecord owner)
    {
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, partition);
        if (owner is null || (!PhysicalOwnerEntryValidation.SameOwner(owner, control.Owner)
            && !PhysicalOwnerEntryValidation.SameOwner(owner, destination.Owner)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
    }

    internal static string Secret(NodeOptions options, PartitionHost partition, PhysicalShardRecord owner)
    {
        RequireOwner(options, partition, owner);
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, partition);
        return PartitionMovementNativeDiscoveryIdentity.Secret(options, partition.Configuration,
            PhysicalOwnerEntryValidation.SameOwner(owner, control.Owner) ? control : destination);
    }

    internal static void RequireShape(RemoteModelNativeRequest request, IOptions<OrleansMembershipOptions> membership)
    {
        if (!Enum.IsDefined(request.Channel) || request.Payload.IsEmpty
            || request.Payload.Length > RemoteDocumentProtocol.MaximumBodyBytes
            || !Signature(request.Signature) || !Signature(request.SenderSignature)
            || request.SourceOwner is null || request.DestinationOwner is null
            || request.SourceOwner.VoterIds.IsDefaultOrEmpty || request.DestinationOwner.VoterIds.IsDefaultOrEmpty
            || request.SourceOwner.VoterIds.Length != RequiredReplicaVoters || request.DestinationOwner.VoterIds.Length != RequiredReplicaVoters
            || string.IsNullOrEmpty(request.CallerVoter) || string.IsNullOrEmpty(request.DestinationVoter)
            || request.CallerVoter.Length > ReplicaTransportProtocol.MaximumIdentityCharacters
            || request.DestinationVoter.Length > ReplicaTransportProtocol.MaximumIdentityCharacters
            || !PhysicalGroupAddress.CanonicalAddress(request.CallerSiloAddress, membership)
            || !PhysicalGroupAddress.CanonicalAddress(request.DestinationSiloAddress, membership)
            || request.Nonce == Guid.Empty || request.ExpiresAt.Offset != TimeSpan.Zero)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteModelNativeProtocol.InvalidProof); }
    }

    internal static bool ReplyShape(RemoteModelNativeReply reply, long maximum)
        => reply.Payload.Length <= maximum && Signature(reply.SenderSignature)
            && (reply.Error is null
                ? !reply.Payload.IsEmpty && Signature(reply.Signature) && reply.SafeDetail is null
                : reply.Payload.IsEmpty && reply.Signature is { Length: EmptySignatureCharacters }
                    && Enum.IsDefined(reply.Error.Value) && reply.SafeDetail == RemoteModelNativeProtocol.Unavailable);

    internal static bool Signature(string? value)
        => value is { Length: RemoteDocumentProtocol.SignatureCharacters }
            && value.All(char.IsAsciiHexDigitLower);
}
