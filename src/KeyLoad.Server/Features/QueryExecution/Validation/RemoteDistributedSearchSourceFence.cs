using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Query.Features.QueryExecution;

namespace KeyLoad.Server.Features.QueryExecution;

internal static class RemoteDistributedSearchSourceFence
{
    internal const string Changed = "The distributed search source authority changed.";

    internal static void RequireSame(PartitionQuerySourceFence expected, PartitionQuerySourceFence actual)
    {
        var before = expected.Authority;
        var after = actual.Authority;
        var left = before.Placement;
        var right = after.Placement;
        if (expected.ResourceDigest != actual.ResourceDigest || before.PrincipalId != after.PrincipalId
            || before.Tenant != after.Tenant || before.PolicyEpoch != after.PolicyEpoch
            || before.DirectoryRevision != after.DirectoryRevision
            || !PhysicalOwnerEntryValidation.Same(before.Destination, after.Destination)
            || left.Version != right.Version || left.Partition != right.Partition
            || left.PhysicalShardId != right.PhysicalShardId || left.Incarnation != right.Incarnation
            || left.PlacementEpoch != right.PlacementEpoch || left.DirectoryRevision != right.DirectoryRevision
            || left.Revision != right.Revision || left.IsFallback != right.IsFallback
            || !left.VoterIds.SequenceEqual(right.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, Changed); }
    }
}
