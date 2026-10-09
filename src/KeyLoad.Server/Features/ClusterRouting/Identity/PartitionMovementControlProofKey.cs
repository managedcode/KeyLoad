using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementControlProofKey
{
    internal static string Resolve(NodeOptions options, ReplicaConfiguration configuration, PhysicalShardRecord controlOwner)
    {
        var actual = PhysicalOwnerConfiguredTuples.Control(options, configuration);
        if (!PhysicalOwnerEntryValidation.SameOwner(actual.Owner, controlOwner))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof); }
        return options.MembershipAuthority.Mode == MembershipAuthoritySettingsProtocol.Authority
            ? options.PeerSecret : options.MembershipAuthority.AuthorityPeerSecret
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMovementProtocol.InvalidProof);
    }
}
