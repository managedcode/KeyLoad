using KeyLoad.Core.GraphTraversal.Serialization;
using KeyLoad.Core.GraphTraversal.Validation;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes GraphTraversalCommands operations against the caller's database context.</summary>
public static class GraphTraversalCommands
{
    internal const string ApplyReceipt = "applyCrossPartitionReverseEdge";
    internal const string ApplyNoChangeReceipt = "applyCrossPartitionReverseEdgeNoChange";
    internal const string CompleteReceipt = "completeCrossPartitionReverseEdge";
    internal const string CompleteNoChangeReceipt = "completeCrossPartitionReverseEdgeNoChange";
    internal const string InconsistentIntent = "The graph delivery intent and canonical edge are inconsistent.";
    internal const string ReceiverConflict = "The graph reverse projection conflicts at the same revision.";
    internal const string ReceiverNotCommitted = "The graph reverse projection is not committed.";
    internal static MutationReceipt ApplyGraphReverseDelivery(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef targetPartition, ApplyCrossPartitionReverseEdge request)
    {
        const string InvalidDeliveryTarget = "The graph delivery target does not match its command partition.";
        GraphCrossPartitionValidation.ValidateLocator(request.SourcePartition, request.Graph, request.EdgeId, request.Destination, request.ExpectedRevision);
        if (targetPartition != request.Destination.Partition || targetPartition == request.SourcePartition)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidDeliveryTarget);
        }

        engine.RequireGraphWrite(tx, principal, request.SourcePartition, targetPartition, request.Graph);
        var intent = CurrentIntent(tx, request.SourcePartition, request.Graph, request.EdgeId, request.Destination);
        if (intent is null || intent.Revision != request.ExpectedRevision)
        {
            return NoChange(ApplyNoChangeReceipt, request.Graph, request.EdgeId, intent?.Revision ?? request.ExpectedRevision);
        }

        return engine.ApplyCurrentIntent(tx, principal, targetPartition, request with { Destination = intent.Destination }, intent);
    }

    internal static MutationReceipt ApplyCurrentIntent(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef targetPartition, ApplyCrossPartitionReverseEdge request, GraphCrossPartitionDeliveryIntent intent)
    {
        var owner = GraphCrossPartitionRecords.Read<GraphEdgeOwnerVersion>(tx, GraphCrossPartitionKeys.OwnerVersion(request.SourcePartition, request.Graph, request.EdgeId));
        GraphCrossPartitionValidation.ValidateOwnerVersion(owner);
        var canonical = tx.GetRecord<EdgeRecord>(GraphTraversalStorage.EdgeKey(request.SourcePartition, request.Graph, request.EdgeId));
        GraphTraversalCommands.VerifyCurrentIntent(intent, owner!, canonical);
        if (!intent.Deleted)
        {
            engine.VisibleVertex(tx, principal, intent.Edge.From);
            engine.VisibleVertex(tx, principal, intent.Edge.To);
        }

        var key = GraphCrossPartitionKeys.Reverse(targetPartition, request.Graph, request.Destination, request.SourcePartition, request.EdgeId);
        var current = GraphCrossPartitionRecords.Read<GraphCrossPartitionReceiverState>(tx, key);
        if (current is not null)
        {
            GraphCrossPartitionValidation.ValidateReceiver(current, request.SourcePartition, request.Graph, request.EdgeId, request.Destination);
            if (current.Revision > intent.Revision)
            {
                return NoChange(ApplyNoChangeReceipt, request.Graph, request.EdgeId, current.Revision);
            }

            if (current.Revision == intent.Revision)
            {
                if (current.Fingerprint != intent.Fingerprint || current.Deleted != intent.Deleted)
                {
                    throw Errors.Fail(ErrorCode.Conflict, ReceiverConflict);
                }

                return NoChange(ApplyNoChangeReceipt, request.Graph, request.EdgeId, current.Revision);
            }
        }

        var state = new GraphCrossPartitionReceiverState(GraphCrossPartitionProtocol.CurrentVersion, request.SourcePartition, request.Graph, request.EdgeId, request.Destination, intent.Revision, intent.Deleted, intent.Fingerprint);
        var encoded = GraphCrossPartitionRecords.Encode(state);
        GraphCrossPartitionCapacityWriter.Apply(tx, targetPartition, GraphCrossPartitionCapacityDirection.ReceiverStates, GraphCrossPartitionKeys.ReversePartitionPrefix(targetPartition), [new(key, encoded)]);
        return new(ApplyReceipt, request.Graph, request.EdgeId, intent.Revision);
    }

    internal static MutationReceipt CompleteGraphReverseDelivery(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef sourcePartition, CompleteCrossPartitionReverseEdge request)
    {
        const string InvalidCompletionSource = "The graph completion source does not match its command partition.";
        GraphCrossPartitionValidation.ValidateLocator(request.SourcePartition, request.Graph, request.EdgeId, request.Destination, request.ExpectedRevision);
        if (sourcePartition != request.SourcePartition || sourcePartition == request.Destination.Partition)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidCompletionSource);
        }

        engine.RequireGraphWrite(tx, principal, sourcePartition, request.Destination.Partition, request.Graph);
        var intent = CurrentIntent(tx, sourcePartition, request.Graph, request.EdgeId, request.Destination);
        if (intent is null || intent.Revision != request.ExpectedRevision)
        {
            return NoChange(CompleteNoChangeReceipt, request.Graph, request.EdgeId, intent?.Revision ?? request.ExpectedRevision);
        }

        request = request with
        {
            Destination = intent.Destination
        };
        var owner = GraphCrossPartitionRecords.Read<GraphEdgeOwnerVersion>(tx, GraphCrossPartitionKeys.OwnerVersion(sourcePartition, request.Graph, request.EdgeId));
        GraphCrossPartitionValidation.ValidateOwnerVersion(owner);
        var canonical = tx.GetRecord<EdgeRecord>(GraphTraversalStorage.EdgeKey(sourcePartition, request.Graph, request.EdgeId));
        GraphTraversalCommands.VerifyCurrentIntent(intent, owner!, canonical);
        var receiverKey = GraphCrossPartitionKeys.Reverse(request.Destination.Partition, request.Graph, request.Destination, sourcePartition, request.EdgeId);
        var receiver = GraphCrossPartitionRecords.Read<GraphCrossPartitionReceiverState>(tx, receiverKey) ?? throw Errors.Fail(ErrorCode.Conflict, ReceiverNotCommitted);
        GraphCrossPartitionValidation.ValidateReceiver(receiver, sourcePartition, request.Graph, request.EdgeId, request.Destination);
        if (receiver.Revision != intent.Revision || receiver.Deleted != intent.Deleted || receiver.Fingerprint != intent.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReceiverNotCommitted);
        }

        GraphCrossPartitionCapacityWriter.Apply(tx, sourcePartition, GraphCrossPartitionCapacityDirection.PendingIntents, GraphCrossPartitionKeys.IntentPrefix(sourcePartition), [new(GraphCrossPartitionKeys.Intent(intent), null)]);
        return new(CompleteReceipt, request.Graph, request.EdgeId, intent.Revision);
    }

    internal static void RequireGraphWrite(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef source, PartitionRef destination, string graph)
    {
        engine.Authorization.Require(principal, source, graph, Capability.GraphWrite);
        engine.Authorization.Require(principal, destination, graph, Capability.GraphWrite);
        _ = engine.Resource(view, source, graph, ResourceKind.Graph);
        _ = engine.Resource(view, destination, graph, ResourceKind.Graph);
    }

    internal static GraphCrossPartitionDeliveryIntent? CurrentIntent(IKeyValueView view, PartitionRef source, string graph, string edgeId, EntityRef destination)
    {
        var key = GraphCrossPartitionKeys.Intent(source, graph, edgeId, destination);
        var intent = GraphCrossPartitionRecords.Read<GraphCrossPartitionDeliveryIntent>(view, key);
        if (intent is not null)
        {
            var definition = ResourceDefinitionBinding.BindResourceDefinition(view, destination.Partition, destination.Collection, ResourceKind.Collection);
            destination = EntityReferenceLifetime.Bind(view, destination, definition);
            EntityReferenceLifetime.RequireStored(view, intent.Destination, definition);
            GraphCrossPartitionValidation.ValidateIntent(intent, source, graph, edgeId, destination);
        }

        return intent;
    }

    internal static void VerifyCurrentIntent(GraphCrossPartitionDeliveryIntent intent, GraphEdgeOwnerVersion owner, EdgeRecord? canonical)
    {
        var canonicalDeleted = owner.Deleted && canonical is null;
        var canonicalActive = !owner.Deleted && canonical is not null && canonical.Revision == owner.Revision;
        if (owner.Revision != intent.Revision || !(canonicalDeleted || canonicalActive) || canonical is not null && !GraphTraversalCommands.CanonicalMatchesOwner(canonical, intent, owner) || !IntentMatchesCanonical(intent, owner, canonical) || GraphCrossPartitionRecords.Fingerprint(intent.SourcePartition, intent.Graph, intent.EdgeId, intent.Destination, intent.Revision, intent.Deleted, intent.Edge) != intent.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, InconsistentIntent);
        }
    }

    internal static bool CanonicalMatchesOwner(EdgeRecord canonical, GraphCrossPartitionDeliveryIntent intent, GraphEdgeOwnerVersion owner)
    {
        GraphCrossPartitionValidation.ValidateEdge(canonical);
        return canonical.Id == intent.EdgeId && canonical.From.Partition == intent.SourcePartition && canonical.Revision == owner.Revision;
    }

    internal static bool IntentMatchesCanonical(GraphCrossPartitionDeliveryIntent intent, GraphEdgeOwnerVersion owner, EdgeRecord? canonical)
    {
        if (!intent.Deleted)
        {
            return !owner.Deleted && canonical == intent.Edge && canonical?.To == intent.Destination;
        }

        return owner.Deleted && canonical is null || !owner.Deleted && canonical is not null && canonical.To != intent.Destination;
    }

    internal static MutationReceipt NoChange(string kind, string graph, string edgeId, long revision) => new(kind, graph, edgeId, revision);
}
