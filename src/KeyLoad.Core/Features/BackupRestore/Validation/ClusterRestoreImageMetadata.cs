using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class ClusterRestoreImageMetadata
{
    private const string MarkerSpace = "cluster-restore-marker";
    private const string MarkerVersion = "v1";
    private const int SingleOwner = 1;
    private const string Invalid = "The recovered native catalog metadata differs from its exact admitted transformation.";
    internal static byte[] MarkerKey() => KeyCodec.Encode(MarkerSpace, MarkerVersion);

    internal static void Require(ClusterRestoreImageState state)
    {
        var catalog = PhysicalShardCatalogRecordSerialization.Read(state.Target);
        if (catalog is null || catalog.Version != PhysicalShardCatalogProtocol.CurrentVersion
            || catalog.Revision != PhysicalShardCatalogProtocol.InitialRevision
            || !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, state.Local.Target))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        if (state.Mappings.Length > SingleOwner)
        {
            var owners = state.Mappings.Select(mapping => new RegisteredPhysicalOwnerV1(mapping.Target, mapping.Endpoints))
                .ToImmutableArray();
            var revision = state.Original.RegisteredOwners?.Revision ?? PhysicalOwnerDirectoryProtocol.RevisionStep;
            var expected = new PhysicalOwnerDirectoryV1(PhysicalOwnerDirectoryProtocol.Version,
                revision, state.Local.Target, owners);
            if (!ClusterBackupMetadataEquality.Registered(expected, PhysicalOwnerDirectorySerialization.Read(state.Target)))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        }
        var marker = state.Target.GetRecord<ClusterRestoreMarker>(MarkerKey());
        if (marker is null || marker.Version != ClusterRestoreMarker.CurrentVersion || marker.SlotContext != state.Context || marker.OriginalCut is null
            || !ClusterBackupMetadataEquality.OwnerCut(state.Original, marker.OriginalCut)
            || ClusterRestoreMappingDigest.Compute(marker.Mappings) != ClusterRestoreMappingDigest.Compute(state.Mappings))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        foreach (var original in state.Original.Partitions)
        {
            state.Work.Check();
            var owner = original.Placement.IsFallback ? state.Local.Target : state.Mappings.Single(mapping =>
                mapping.Source.PhysicalShardId == original.Placement.PhysicalShardId).Target;
            var expected = original.Placement with
            {
                PhysicalShardId = owner.PhysicalShardId,
                Incarnation = owner.Incarnation,
                VoterIds = owner.VoterIds,
                PlacementEpoch = owner.PlacementEpoch
            };
            if (!ClusterBackupMetadataEquality.Placement(expected, DatabaseEngine.ReadClusterBackupPlacement(
                state.Target, original.Roster.Partition, state.Local.Target)))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        }
    }

    internal static List<byte[]> ChangedKeys(ClusterRestoreImageState state)
    {
        List<byte[]> keys = [PhysicalShardCatalogRecordSerialization.CatalogKey(), MarkerKey(),
            ClusterRestoreSlotKeys.Reconciled(), ClusterRestoreSlotKeys.AuthorityReset()];
        if (state.Mappings.Length > SingleOwner)
        { keys.Add(PhysicalOwnerDirectorySerialization.Key()); }
        foreach (var partition in state.Original.Partitions)
        {
            if (!partition.Placement.IsFallback)
            { keys.Add(AtomicPartitionPlacementSerialization.RowKey(partition.Roster.Partition)); }
        }
        if (state.Reset)
        { keys.AddRange(ClusterRestoreImageOrigins.ChangedKeys(state)); }
        return keys;
    }
}
