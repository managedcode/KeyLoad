using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.GraphTraversal;

internal static class RemoteGraphNativeIdentity
{
    internal static void RequireOwner(NodeOptions options, PartitionHost partition, RegisteredPhysicalOwnerV1 owner)
    {
        if (!PhysicalOwnerEntryValidation.Valid(owner))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof);
        }
        var control = PhysicalOwnerConfiguredTuples.Control(options, partition);
        var destination = PhysicalOwnerConfiguredTuples.Destination(options, partition);
        if (!PhysicalOwnerEntryValidation.Same(owner, control) && !PhysicalOwnerEntryValidation.Same(owner, destination))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof);
        }
    }

    internal static void RequireShape(RemoteGraphNativeRequestV1 request, int maximumBytes,
        IOptions<OrleansMembershipOptions> membership)
    {
        if (request is null || request.Version != RemoteGraphNativeProtocol.Version || !Enum.IsDefined(request.Action)
            || request.RequestId == Guid.Empty || request.Nonce == Guid.Empty || request.Payload.IsEmpty
            || request.Payload.Length > maximumBytes || !Signature(request.Signature)
            || !PhysicalOwnerEntryValidation.Valid(request.SourceOwner) || !PhysicalOwnerEntryValidation.Valid(request.DestinationOwner)
            || string.IsNullOrWhiteSpace(request.PrincipalId) || request.PrincipalId.Length > maximumBytes
            || string.IsNullOrEmpty(request.CallerVoter) || string.IsNullOrEmpty(request.DestinationVoter)
            || request.CallerVoter.Length > ReplicaTransportProtocol.MaximumIdentityCharacters
            || request.DestinationVoter.Length > ReplicaTransportProtocol.MaximumIdentityCharacters
            || !request.SourceOwner.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !request.DestinationOwner.Owner.VoterIds.Contains(request.DestinationVoter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(request.CallerSiloAddress, membership)
            || !PhysicalGroupAddress.CanonicalAddress(request.DestinationSiloAddress, membership)
            || request.ExpiresAt.Offset != TimeSpan.Zero)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphNativeProtocol.InvalidProof);
        }
        JsonData.Identifier(request.PrincipalId);
    }

    internal static bool ReplyShape(RemoteGraphNativeReplyV1 reply, long maximum)
        => reply is not null && reply.Version == RemoteGraphNativeProtocol.Version
            && reply.RequestId != Guid.Empty && reply.Nonce != Guid.Empty && reply.ReceiverDiscovery is not null
            && reply.Payload.Length <= maximum && Signature(reply.Signature)
            && Bounded(reply.ReceiverDiscovery.VoterId, ReplicaTransportProtocol.MaximumIdentityCharacters)
            && Bounded(reply.ReceiverDiscovery.ClusterId, ReplicaTransportProtocol.MaximumClusterCharacters)
            && Bounded(reply.ReceiverDiscovery.SiloAddress, ReplicaTransportProtocol.MaximumAddressCharacters)
            && (reply.Error is null
                ? !reply.Payload.IsEmpty && reply.SafeDetail is null
                : reply.Payload.IsEmpty && Enum.IsDefined(reply.Error.Value) && reply.SafeDetail == RemoteGraphNativeProtocol.Unavailable);

    private static bool Bounded(string? value, int maximum)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum;

    internal static bool Signature(string? value)
        => value is { Length: ReplicaTransportProtocol.HashHexCharacters } && value.All(char.IsAsciiHexDigitLower);
}
