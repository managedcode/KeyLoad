using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static ImmutableArray<RemoteGraphIntentV1> BuildRemoteGraphSourceIntents(IKeyValueView view,
        PrincipalRecord principal, RemoteGraphSourceMutationV1 command, EdgeRecord? previous,
        EdgeRecord? next, CommitToken sourceCommit, long revision)
    {
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        var targets = RemoteGraphSourceDestinations(previous, next, revision);
        if (targets.Length != command.Preparations.Length)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
        }
        var graph = command.Original.Upsert?.Graph ?? command.Original.Delete!.Graph;
        var edgeId = command.Original.Upsert?.EdgeId ?? command.Original.Delete!.EdgeId;
        var result = ImmutableArray.CreateBuilder<RemoteGraphIntentV1>(targets.Length);
        foreach (var (target, image, deleted) in targets)
        {
            var prepared = command.Preparations.Where(value => value.Preparation.Request.Destination == target
                && value.Preparation.Request.Deleted == deleted).ToArray();
            if (prepared.Length != RemoteGraphSinglePreparation)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
            }
            var placement = ResolveRegisteredPlacement(view, target.Partition, catalog.DefaultShard);
            var directory = PhysicalOwnerDirectorySerialization.Read(view)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
            var destination = directory.Owners.SingleOrDefault(value => value.Owner.PhysicalShardId == placement.PhysicalShardId)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
            if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, catalog.DefaultShard)
                || destination.Owner.Incarnation != placement.Incarnation
                || !destination.Owner.VoterIds.SequenceEqual(placement.VoterIds, StringComparer.Ordinal)
                || PhysicalOwnerEntryValidation.SameOwner(destination.Owner, catalog.DefaultShard)
                || !NativeSerialization.Serialize(placement).AsSpan().SequenceEqual(
                    NativeSerialization.Serialize(prepared[RemoteGraphFirstPreparation].Preparation.Cut.Placement))
                || !PhysicalOwnerEntryValidation.Same(destination, prepared[RemoteGraphFirstPreparation].Preparation.Request.DestinationOwner))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
            }
            var content = new GraphCrossPartitionDeliveryIntentV1(GraphCrossPartitionProtocol.CurrentVersion,
                command.Original.SourcePartition, graph, edgeId, target, revision, deleted, image,
                principal.Id, principal.PolicyEpoch, GraphCrossPartitionRecords.Fingerprint(
                    command.Original.SourcePartition, graph, edgeId, target, revision, deleted, image));
            result.Add(new(RemoteGraphProtocol.Version, content, catalog.DefaultShard, destination,
                sourceCommit, command.Original.CommandId));
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<(EntityRef Target, EdgeRecord Image, bool Deleted)> RemoteGraphSourceDestinations(
        EdgeRecord? previous, EdgeRecord? next, long revision)
    {
        var result = ImmutableArray.CreateBuilder<(EntityRef, EdgeRecord, bool)>();
        if (previous is not null && (next is null || previous.To != next.To))
        {
            result.Add((previous.To, previous with
            {
                Revision = revision
            }, true));
        }
        if (next is not null)
        {
            result.Add((next.To, next, false));
        }
        return result.ToImmutable();
    }

    private const int RemoteGraphSinglePreparation = 1;
    private const int RemoteGraphFirstPreparation = 0;
}
