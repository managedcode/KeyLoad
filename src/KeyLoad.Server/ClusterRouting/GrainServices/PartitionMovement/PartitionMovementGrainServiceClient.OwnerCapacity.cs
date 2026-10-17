using KeyLoad.Core.Features.ClusterRouting.Contracts;

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
        request = request with
        {
            Signature = OwnerCapacityMac.SignCapacityRequest(mac, request)
        };
        var reply = await ReadCapacityPacketAsync(actual, request, token).ConfigureAwait(false);
        return OwnerCapacityNativeReplyValidation.Require(partition, clock, request, reply, mac, token);
    }
}
