using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementGrainServiceClient
{
    internal Task<OwnerCapacityNativeReply> ReadCapacityPacketAsync(SiloAddress destination,
        OwnerCapacityNativeRequest request, CancellationToken token)
        => GetGrainService(destination).ReadCapacityAsync(request, token);

    internal async Task<OwnerCapacitySnapshotV1> ReadCapacityAsync(RegisteredPhysicalOwnerV1 destination,
        int voterIndex, PrincipalRecord principal, string cohort, System.Collections.Immutable.ImmutableArray<PartitionRef> partitions,
        DateTimeOffset originalExpiry, long maximumReadBytes, int maximumRecords, int maximumResultBytes, CancellationToken token)
    {
        var actual = await ResolveAsync(destination, voterIndex, token).ConfigureAwait(false);
        var caller = local.Read();
        var request = new OwnerCapacityNativeRequest(TenantPlacementPolicyProtocol.Version, Guid.NewGuid(),
            Guid.NewGuid(), principal.Id, principal.PolicyEpoch,
            PhysicalOwnerConfiguredTuples.Control(options, partition.Configuration), destination,
            caller.VoterId, caller.SiloAddress, destination.Owner.VoterIds[voterIndex], actual.ToParsableString(),
            cohort, clock.GetUtcNow(), originalExpiry, partitions, string.Empty, maximumReadBytes, maximumRecords, maximumResultBytes);
        using var mac = CreateMac(ControlSecret());
        request = request with { Signature = OwnerCapacityMac.SignCapacityRequest(mac, request) };
        var reply = await ReadCapacityPacketAsync(actual, request, token).ConfigureAwait(false);
        return RequireCapacityReply(request, reply, mac, token);
    }

    private OwnerCapacitySnapshotV1 RequireCapacityReply(OwnerCapacityNativeRequest request,
        OwnerCapacityNativeReply reply, PartitionMovementMac mac, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (reply is null || reply.Version != request.Version || reply.RequestId != request.RequestId
            || reply.Nonce != request.Nonce || reply.Signature is not { Length: ReplicaTransportProtocol.HashHexCharacters }
            || NativeSerialization.Measure(reply) > partition.Database.Limits.MaxBatchBytes
            || !OwnerCapacityMac.VerifyCapacityReply(mac, reply))
        { throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof); }
        if (reply.Error is { } error)
        {
            if (!Enum.IsDefined(error) || reply.Snapshot is not null
                || reply.SafeDetail != TenantPlacementCapacityProtocol.Unavailable)
            { throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof); }
            throw Errors.Fail(error, TenantPlacementCapacityProtocol.Unavailable);
        }
        var value = reply.Snapshot;
        if (reply.SafeDetail is not null || value is null || value.RequestId != request.RequestId
            || value.Nonce != request.Nonce || value.CohortFingerprint != request.CohortFingerprint
            || value.SiloAddress != request.DestinationSiloAddress || value.VoterId != request.DestinationVoter
            || !Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(value.Owner, request.Destination.Owner)
            || value.Resource.ProcessDomain != request.DestinationSiloAddress
            || value.Resource.ExpiresAt <= clock.GetUtcNow() || request.ExpiresAt <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementCapacityProtocol.InvalidProof); }
        return value;
    }
}
