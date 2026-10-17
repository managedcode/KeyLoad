using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRemoteGraphForeignIntent(IKeyValueView view, RemoteGraphSourceProofV1 proof)
    {
        if (proof is null || proof.Version != RemoteGraphProtocol.Version || proof.Intent is null
            || proof.Intent.Content is null || proof.Cut is null || proof.Cut.Owner is null
            || proof.Cut.Placement is null || proof.Cut.NodeId == Guid.Empty || proof.Cut.ReadGeneration < RemoteGraphFirstGeneration)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
        }
        var intent = proof.Intent;
        var content = intent.Content;
        if (intent.Version != RemoteGraphProtocol.Version || intent.SourceOwner is null || intent.SourceCommit is null
            || intent.SourceCommandId == Guid.Empty || !PhysicalOwnerEntryValidation.Valid(intent.DestinationOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(intent.SourceOwner, proof.Cut.Owner)
            || PhysicalOwnerEntryValidation.SameOwner(intent.SourceOwner, intent.DestinationOwner.Owner))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
        }
        GraphCrossPartitionValidation.ValidateIntent(content, content.SourcePartition, content.Graph,
            content.EdgeId, content.Destination, Limits);
        if (GraphCrossPartitionRecords.Fingerprint(content.SourcePartition, content.Graph, content.EdgeId,
            content.Destination, content.Revision, content.Deleted, content.Edge) != content.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
        }
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        var localCatalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        var source = directory.Owners.SingleOrDefault(value => PhysicalOwnerEntryValidation.SameOwner(
            value.Owner, intent.SourceOwner));
        var destination = directory.Owners.SingleOrDefault(value => PhysicalOwnerEntryValidation.Same(
            value, intent.DestinationOwner));
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, localCatalog.DefaultShard)
            || source is null || destination is null || proof.Original is null
            || proof.Original.CommandId != intent.SourceCommandId
            || proof.Original.SourcePartition != content.SourcePartition
            || (proof.Original.Upsert is null) == (proof.Original.Delete is null)
            || (proof.Original.Upsert?.Graph ?? proof.Original.Delete?.Graph) != content.Graph
            || (proof.Original.Upsert?.EdgeId ?? proof.Original.Delete?.EdgeId) != content.EdgeId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        }
        var placement = proof.Cut.Placement;
        if (placement.Partition != content.SourcePartition || proof.Cut.Incarnation != placement.Incarnation
            || placement.PhysicalShardId != intent.SourceOwner.PhysicalShardId
            || placement.Incarnation != source.Owner.Incarnation
            || !placement.VoterIds.SequenceEqual(source.Owner.VoterIds, StringComparer.Ordinal)
            || placement.PlacementEpoch < RemoteGraphFirstGeneration
            || proof.Cut.StorePosition < RemoteGraphAbsentRevision
            || proof.Cut.AppliedPosition < intent.SourceCommit.Position
            || intent.SourceCommit.Position < RemoteGraphFirstGeneration
            || intent.SourceCommit.Incarnation != placement.Incarnation
            || intent.SourceCommit.OwnershipEpoch != placement.PlacementEpoch
            || intent.SourceCommit.AtomicPartitionId != content.SourcePartition.AtomicPartitionId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        }
    }

    private const int RemoteGraphFirstGeneration = 1;
}
