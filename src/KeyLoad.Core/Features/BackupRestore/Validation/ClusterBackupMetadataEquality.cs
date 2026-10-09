using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

internal static class ClusterBackupMetadataEquality
{
    internal static bool Placement(AtomicPartitionPlacementResolution expected, AtomicPartitionPlacementResolution actual)
        => expected.Version == actual.Version && expected.Partition == actual.Partition
            && expected.PhysicalShardId == actual.PhysicalShardId && expected.Incarnation == actual.Incarnation
            && expected.PlacementEpoch == actual.PlacementEpoch && expected.DirectoryRevision == actual.DirectoryRevision
            && expected.Revision == actual.Revision && expected.IsFallback == actual.IsFallback
            && !expected.VoterIds.IsDefault && expected.VoterIds.SequenceEqual(actual.VoterIds, StringComparer.Ordinal);

    internal static bool Registered(PhysicalOwnerDirectoryV1? expected, PhysicalOwnerDirectoryV1? actual)
    {
        if (expected is null || actual is null)
        { return expected is null && actual is null; }
        if (expected.ControlOwner is null || expected.ControlOwner.VoterIds.IsDefault || expected.Owners.IsDefault || expected.Version != actual.Version
            || expected.Revision != actual.Revision || expected.Owners.Length != actual.Owners.Length
            || !PhysicalOwnerEntryValidation.SameOwner(expected.ControlOwner, actual.ControlOwner))
        { return false; }
        return expected.Owners.Zip(actual.Owners).All(pair => pair.First is not null && pair.First.Owner is not null
            && !pair.First.Owner.VoterIds.IsDefault && !pair.First.Endpoints.IsDefault
            && PhysicalOwnerEntryValidation.Same(pair.First, pair.Second));
    }

    internal static bool OwnerCut(ClusterBackupOwnerCut expected, ClusterBackupOwnerCut actual) =>
        expected.Version == actual.Version && expected.CaptureId == actual.CaptureId
        && expected.SourceNodeId == actual.SourceNodeId && expected.StorePosition == actual.StorePosition
        && expected.AppliedIndex == actual.AppliedIndex
        && string.Equals(expected.CanonicalDigest, actual.CanonicalDigest, StringComparison.Ordinal)
        && expected.Owner is not null && !expected.Owner.VoterIds.IsDefault
        && PhysicalOwnerEntryValidation.SameOwner(expected.Owner, actual.Owner)
        && expected.Catalog is not null && expected.Catalog.DefaultShard is not null
        && !expected.Catalog.DefaultShard.VoterIds.IsDefault
        && expected.Catalog.Version == actual.Catalog.Version && expected.Catalog.Revision == actual.Catalog.Revision
        && PhysicalOwnerEntryValidation.SameOwner(expected.Catalog.DefaultShard, actual.Catalog.DefaultShard)
        && expected.Directory == actual.Directory && Registered(expected.RegisteredOwners, actual.RegisteredOwners)
        && !expected.Partitions.IsDefault && expected.Partitions.Length == actual.Partitions.Length
        && expected.Partitions.Zip(actual.Partitions).All(pair => Partition(pair.First, pair.Second));

    private static bool Partition(ClusterBackupPartitionCut expected, ClusterBackupPartitionCut actual) =>
        expected is not null && expected.Version == actual.Version && expected.Roster == actual.Roster
        && expected.AppliedIndex == actual.AppliedIndex && expected.CanonicalRecordCount == actual.CanonicalRecordCount
        && string.Equals(expected.RosterDigest, actual.RosterDigest, StringComparison.Ordinal)
        && string.Equals(expected.CanonicalDigest, actual.CanonicalDigest, StringComparison.Ordinal)
        && expected.Placement is not null && Placement(expected.Placement, actual.Placement);

}
