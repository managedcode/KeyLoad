using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRemoteGraphStoredIntent(IKeyValueView view, RemoteGraphSourceReadRequestV1 request,
        RemoteGraphIntentV1? intent)
        => RequireRemoteGraphStoredIntent(view, request.SourcePartition, request.Graph, request.EdgeId,
            request.SourceOwner, intent);

    private void RequireRemoteGraphStoredIntent(IKeyValueView view, PartitionRef sourcePartition,
        string graph, string edgeId, PhysicalShardRecord sourceOwner, RemoteGraphIntentV1? intent)
    {
        if (intent is null || intent.Version != RemoteGraphProtocol.Version || intent.Content is null
            || intent.SourceOwner is null || intent.SourceCommit is null || intent.SourceCommandId == Guid.Empty
            || !PhysicalOwnerEntryValidation.Valid(intent.DestinationOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(intent.SourceOwner, sourceOwner)
            || PhysicalOwnerEntryValidation.SameOwner(intent.SourceOwner, intent.DestinationOwner.Owner))
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        GraphCrossPartitionValidation.ValidateIntent(intent.Content, sourcePartition, graph,
            edgeId, intent.Content.Destination, Limits);
        var expected = GraphCrossPartitionRecords.Fingerprint(sourcePartition, graph,
            edgeId, intent.Content.Destination, intent.Content.Revision, intent.Content.Deleted,
            intent.Content.Edge);
        if (!string.Equals(expected, intent.Content.Fingerprint, StringComparison.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        ValidateCommitToken(view, sourcePartition, intent.SourceCommit,
            ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        var destination = directory.Owners.SingleOrDefault(value => PhysicalOwnerEntryValidation.Same(
            value, intent.DestinationOwner));
        var registered = ResolveRegisteredPlacementOwner(view, catalog.DefaultShard,
            intent.DestinationOwner.Owner.PhysicalShardId, ErrorCode.OwnershipLost);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, sourceOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, catalog.DefaultShard)
            || destination is null || !PhysicalOwnerEntryValidation.SameOwner(registered, intent.DestinationOwner.Owner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        }
    }
}
