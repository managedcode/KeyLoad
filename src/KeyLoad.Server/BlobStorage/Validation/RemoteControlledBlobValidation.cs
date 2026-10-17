using KeyLoad.Clustering;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal static class RemoteControlledBlobValidation
{
    internal static void Require(OrleansNode node, PartitionHost partition, IOptions<NodeOptions> options,
        IOptions<OrleansMembershipOptions> membership, IOptions<GrainRoutingOptions> routing,
        TimeProvider clock, RemoteControlledBlobCall call, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settings = options.Value.PhysicalGroupTrust;
        var request = call.Request;
        var frame = request?.Frame;
        var now = clock.GetUtcNow();
        if (!settings.RemoteDocumentReads || !settings.RegisterPhysicalOwners
            || settings.Role != PhysicalGroupTrustRole.Destination
            || call.Version != RemoteDocumentProtocol.Version || call.RequestId == Guid.Empty
            || !RemoteModelNativeIdentity.ValidNonce(call.Nonce)
            || !PhysicalOwnerConfiguredTuples.Control(options.Value, partition).Owner.VoterIds.Contains(call.CallerVoter, StringComparer.Ordinal)
            || !PhysicalGroupAddress.CanonicalAddress(call.CallerSiloAddress, membership)
            || request is null || frame is null || frame.ExpiresAt <= now || frame.ExpiresAt > now + routing.Value.RequestLifetime
            || call.Source is null || call.Destination is null
            || !PhysicalOwnerEntryValidation.SameOwner(call.Source, PhysicalOwnerConfiguredTuples.Control(options.Value, partition).Owner)
            || !PhysicalOwnerEntryValidation.Same(call.Destination, PhysicalOwnerConfiguredTuples.Local(options.Value, partition))
            || frame.Publication is null || frame.Control is null
            || !PhysicalOwnerEntryValidation.SameOwner(frame.Publication.Destination, call.Destination.Owner)
            || call.MaximumReplyBytes <= RemoteControlledBlobProtocol.EmptyCount
            || call.MaximumReplyBytes > partition.Database.Limits.MaxBatchBytes
            || request.MaximumResultBytes > call.MaximumReplyBytes)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        if (BlobControlledReadScope.Require(frame).Partition != frame.Control.Partition)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        _ = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
    }
}
