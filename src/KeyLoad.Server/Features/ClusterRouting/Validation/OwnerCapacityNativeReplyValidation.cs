using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Validates the authenticated bounded native owner-capacity reply against its original request.</summary>
internal static class OwnerCapacityNativeReplyValidation
{
    internal static OwnerCapacitySnapshotV1 Require(PartitionHost partition, TimeProvider clock,
        OwnerCapacityNativeRequest request, OwnerCapacityNativeReply reply,
        PartitionMovementMac mac, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (reply is null || reply.Version != request.Version || reply.RequestId != request.RequestId
            || reply.Nonce != request.Nonce || reply.Signature is not { Length: ReplicaTransportProtocol.HashHexCharacters }
            || NativeSerialization.Measure(reply) > partition.Database.Limits.MaxBatchBytes
            || !OwnerCapacityMac.VerifyCapacityReply(mac, reply))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof);
        }
        if (reply.Error is { } error)
        {
            if (!Enum.IsDefined(error) || reply.Snapshot is not null
                || reply.SafeDetail != TenantPlacementCapacityProtocol.Unavailable)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof);
            }
            throw Errors.Fail(error, TenantPlacementCapacityProtocol.Unavailable);
        }
        var value = reply.Snapshot;
        if (reply.SafeDetail is not null || value is null || value.RequestId != request.RequestId
            || value.Nonce != request.Nonce || value.CohortFingerprint != request.CohortFingerprint
            || value.SiloAddress != request.DestinationSiloAddress || value.VoterId != request.DestinationVoter
            || !Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(value.Owner, request.Destination.Owner)
            || value.Resource.ProcessDomain != request.DestinationSiloAddress
            || value.Resource.ExpiresAt <= clock.GetUtcNow() || request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof);
        }
        return value;
    }
}
