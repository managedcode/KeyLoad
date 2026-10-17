using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server.Features.DocumentStorage;

internal static class RemoteResourceLifetimeReadValidation
{
    internal static void RequireCall(RemoteResourceLifetimeReadCallV2 call,
        ServerRuntimeOptions options, PartitionHost partition, TimeProvider clock, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var node = options.Node.Value;
        var settings = node.PhysicalGroupTrust;
        var now = clock.GetUtcNow();
        if (call is null || call.Version != RemoteResourceLifetimeReadProtocol.Version
            || !Enum.IsDefined(call.Kind) || call.RequestId == Guid.Empty
            || !RemoteModelNativeIdentity.ValidNonce(call.Nonce) || call.Payload.IsEmpty
            || call.Payload.Length > RemoteDocumentProtocol.MaximumBodyBytes
            || call.MaximumReplyBytes < RemoteResourceLifetimeReadProtocol.MinimumBytes
            || call.MaximumReplyBytes > RemoteDocumentProtocol.MaximumBodyBytes
            || call.ExpiresAt.Offset != TimeSpan.Zero || call.ExpiresAt <= now
            || call.ExpiresAt > now + options.GrainRouting.Value.RequestLifetime
            || !settings.RemoteDocumentReads || !settings.RegisterPhysicalOwners
            || settings.Role != PhysicalGroupTrustRole.Destination
            || call.Kind is (RemoteResourceLifetimeReadKindV2.PartitionQuery or RemoteResourceLifetimeReadKindV2.DistributedSearch)
                && !settings.RemotePartitionQueries
            || !PhysicalGroupAddress.CanonicalAddress(call.CallerSiloAddress, options.Membership)
            || call.Source is null || call.Destination is null
            || !PhysicalOwnerEntryValidation.SameOwner(call.Source, PhysicalOwnerConfiguredTuples.Control(node, partition).Owner)
            || !call.Source.VoterIds.Contains(call.CallerVoter, StringComparer.Ordinal)
            || !PhysicalOwnerEntryValidation.Same(call.Destination, PhysicalOwnerConfiguredTuples.Local(node, partition)))
        {
            throw Invalid();
        }
    }

    internal static void RequireEnvelope(RemoteModelNativeRequest request, RemoteResourceLifetimeReadCallV2 call)
    {
        if (request.Channel != RemoteModelNativeChannel.ResourceLifetimeRead
            || !PhysicalOwnerEntryValidation.SameOwner(request.SourceOwner, call.Source)
            || !PhysicalOwnerEntryValidation.SameOwner(request.DestinationOwner, call.Destination.Owner)
            || request.CallerVoter != call.CallerVoter || request.CallerSiloAddress != call.CallerSiloAddress
            || request.ExpiresAt != call.ExpiresAt)
        {
            throw Invalid();
        }
    }

    internal static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, RemoteResourceLifetimeReadProtocol.InvalidProof);
}
