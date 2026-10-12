using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long RemoteGraphMinimumPosition = 1;

    private static void RequireRemoteGraphPreparation(RemoteGraphSourceIdentityV1 original,
        PrincipalRecord principal, PhysicalShardRecord sourceOwner, RemoteGraphTargetPreparationV1? preparation)
    {
        if (preparation is null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid); }
        var request = preparation.Request;
        var cut = preparation.Cut;
        if (preparation.Version != RemoteGraphProtocol.Version || request is null || cut is null
            || request.OriginalCommandId != original.CommandId || request.PrincipalId != principal.Id
            || request.SourcePartition != original.SourcePartition
            || request.Graph != (original.Upsert?.Graph ?? original.Delete!.Graph)
            || request.Destination is null || request.SourceOwner is null || cut.Owner is null || cut.Placement is null
            || request.Destination.Partition == original.SourcePartition
            || !PhysicalOwnerEntryValidation.Valid(request.DestinationOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(sourceOwner, request.SourceOwner)
            || PhysicalOwnerEntryValidation.SameOwner(sourceOwner, request.DestinationOwner.Owner)
            || !PhysicalOwnerEntryValidation.SameOwner(cut.Owner, request.DestinationOwner.Owner)
            || cut.Placement.Partition != request.Destination.Partition
            || cut.Placement.PhysicalShardId != cut.Owner.PhysicalShardId
            || cut.Placement.Incarnation != cut.Owner.Incarnation
            || cut.Placement.PlacementEpoch < RemoteGraphMinimumPosition
            || !cut.Placement.VoterIds.SequenceEqual(cut.Owner.VoterIds, StringComparer.Ordinal)
            || cut.NodeId == Guid.Empty || cut.Incarnation != cut.Owner.Incarnation
            || cut.StorePosition < RemoteGraphMinimumPosition || cut.SchemaVersion < RemoteGraphMinimumPosition
            || cut.AppliedPosition < RemoteGraphAbsentRevision || cut.ReadGeneration < RemoteGraphAbsentRevision
            || preparation.CollectionSchemaVersion < RemoteGraphMinimumPosition
            || !request.Deleted && preparation.DocumentRevision is not >= RemoteGraphMinimumPosition)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid); }
        ValidatePartition(request.Destination.Partition);
        JsonData.Identifier(request.Destination.Collection);
        JsonData.Identifier(request.Destination.Id);
    }
}
