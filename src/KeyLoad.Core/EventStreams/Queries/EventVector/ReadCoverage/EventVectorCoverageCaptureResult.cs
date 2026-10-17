using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static EventVectorCoverageCapture EventVectorCaptureResult(EventFeedControlRequest request,
        int groupOrdinal, PhysicalShardRecord sourceOwner, PrincipalRecord principal, StoreIdentity identity,
        long storeCut, long appliedCut, EventVectorCoverageCatalog catalog, EventVectorRosterReadResult roster,
        EventVectorCoverageRow? directory, ImmutableArray<EventVectorCoverageRow>.Builder placements,
        ImmutableArray<EventVectorCoverageRow>.Builder heads, ReadOnlyMemory<byte> entries)
        => new()
        {
            Version = EventVectorCaptureVersion,
            MapId = request.MapId,
            ControlPartition = request.ControlPartition,
            PrincipalId = principal.Id,
            Scope = request.Scope,
            GroupOrdinal = groupOrdinal,
            SourceOwner = sourceOwner,
            NodeId = identity.NodeId,
            ReadGeneration = identity.ReadGeneration,
            StoreCutPosition = storeCut,
            AppliedCutPosition = appliedCut,
            PolicyEpoch = principal.PolicyEpoch,
            OriginalEncodedEntries = entries,
            EntryChecksum = EventVectorEntryEncoding.Digest(entries),
            PhysicalCatalog = catalog.CatalogRow,
            RegisteredOwnerRows = [catalog.OwnersRow],
            PlacementRows = placements.ToArray(),
            LogicalDirectory = directory,
            HasMore = false,
            AtomicPartitionRosterRows = roster.Rows.ToArray(),
            SourceHeadRows = heads.ToArray(),
            RosterOriginRows = roster.Origins.ToArray(),
            RosterRestoreIdentity = roster.RestoreIdentity,
        };
}
