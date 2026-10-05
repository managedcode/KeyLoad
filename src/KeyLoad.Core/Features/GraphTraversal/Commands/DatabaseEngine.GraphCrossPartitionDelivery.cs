using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ApplyReceipt = "applyCrossPartitionReverseEdge";
    private const string ApplyNoChangeReceipt = "applyCrossPartitionReverseEdgeNoChange";
    private const string CompleteReceipt = "completeCrossPartitionReverseEdge";
    private const string CompleteNoChangeReceipt = "completeCrossPartitionReverseEdgeNoChange";
    private const string InconsistentIntent = "The graph delivery intent and canonical edge are inconsistent.";
    private const string ReceiverConflict = "The graph reverse projection conflicts at the same revision.";
    private const string ReceiverNotCommitted = "The graph reverse projection is not committed.";

    private MutationReceipt ApplyGraphReverseDelivery(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef targetPartition, ApplyCrossPartitionReverseEdge request)
    {
        GraphCrossPartitionValidation.ValidateLocator(request.SourcePartition, request.Graph, request.EdgeId,
            request.Destination, request.ExpectedRevision);
        if (targetPartition != request.Destination.Partition || targetPartition == request.SourcePartition)
        {
            throw Errors.Fail(ErrorCode.Validation, "The graph delivery target does not match its command partition.");
        }
        RequireGraphWrite(tx, principal, request.SourcePartition, targetPartition, request.Graph);
        RequireSameGraphOwner(tx, request.SourcePartition, targetPartition);
        var intent = CurrentIntent(tx, request.SourcePartition, request.Graph, request.EdgeId,
            request.Destination, Limits);
        if (intent is null || intent.Revision != request.ExpectedRevision)
        {
            return NoChange(ApplyNoChangeReceipt, request.Graph, request.EdgeId,
                intent?.Revision ?? request.ExpectedRevision);
        }
        return ApplyCurrentIntent(tx, principal, targetPartition, request, intent);
    }

    private MutationReceipt ApplyCurrentIntent(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef targetPartition, ApplyCrossPartitionReverseEdge request,
        GraphCrossPartitionDeliveryIntentV1 intent)
    {
        var owner = GraphCrossPartitionRecords.Read<GraphEdgeOwnerVersionV1>(tx,
            GraphCrossPartitionKeys.OwnerVersion(request.SourcePartition, request.Graph, request.EdgeId));
        GraphCrossPartitionValidation.ValidateOwnerVersion(owner);
        var canonical = tx.GetRecord<EdgeRecord>(EdgeKey(request.SourcePartition, request.Graph, request.EdgeId));
        VerifyCurrentIntent(intent, owner!, canonical);
        if (!intent.Deleted)
        {
            VisibleVertex(tx, principal, intent.Edge.From);
            VisibleVertex(tx, principal, intent.Edge.To);
        }
        var key = GraphCrossPartitionKeys.Reverse(targetPartition, request.Graph, request.Destination,
            request.SourcePartition, request.EdgeId);
        var current = GraphCrossPartitionRecords.Read<GraphCrossPartitionReceiverStateV1>(tx, key);
        if (current is not null)
        {
            GraphCrossPartitionValidation.ValidateReceiver(current, request.SourcePartition,
                request.Graph, request.EdgeId, request.Destination);
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
        var state = new GraphCrossPartitionReceiverStateV1(GraphCrossPartitionProtocol.CurrentVersion,
            request.SourcePartition, request.Graph, request.EdgeId, request.Destination,
            intent.Revision, intent.Deleted, intent.Fingerprint);
        var encoded = GraphCrossPartitionRecords.Encode(state, Limits.MaxBatchBytes);
        GraphCrossPartitionCapacityWriter.Apply(tx, targetPartition,
            GraphCrossPartitionCapacityDirection.ReceiverStates,
            GraphCrossPartitionKeys.ReversePartitionPrefix(targetPartition), [new(key, encoded)], Limits);
        return new(ApplyReceipt, request.Graph, request.EdgeId, intent.Revision);
    }

    private MutationReceipt CompleteGraphReverseDelivery(IAtomicTransaction tx,
        PrincipalRecord principal, PartitionRef sourcePartition, CompleteCrossPartitionReverseEdge request)
    {
        GraphCrossPartitionValidation.ValidateLocator(request.SourcePartition, request.Graph, request.EdgeId,
            request.Destination, request.ExpectedRevision);
        if (sourcePartition != request.SourcePartition || sourcePartition == request.Destination.Partition)
        {
            throw Errors.Fail(ErrorCode.Validation, "The graph completion source does not match its command partition.");
        }
        RequireGraphWrite(tx, principal, sourcePartition, request.Destination.Partition, request.Graph);
        RequireSameGraphOwner(tx, sourcePartition, request.Destination.Partition);
        var intent = CurrentIntent(tx, sourcePartition, request.Graph, request.EdgeId,
            request.Destination, Limits);
        if (intent is null || intent.Revision != request.ExpectedRevision)
        {
            return NoChange(CompleteNoChangeReceipt, request.Graph, request.EdgeId,
                intent?.Revision ?? request.ExpectedRevision);
        }
        var owner = GraphCrossPartitionRecords.Read<GraphEdgeOwnerVersionV1>(tx,
            GraphCrossPartitionKeys.OwnerVersion(sourcePartition, request.Graph, request.EdgeId));
        GraphCrossPartitionValidation.ValidateOwnerVersion(owner);
        var canonical = tx.GetRecord<EdgeRecord>(EdgeKey(sourcePartition, request.Graph, request.EdgeId));
        VerifyCurrentIntent(intent, owner!, canonical);
        var receiverKey = GraphCrossPartitionKeys.Reverse(request.Destination.Partition, request.Graph,
            request.Destination, sourcePartition, request.EdgeId);
        var receiver = GraphCrossPartitionRecords.Read<GraphCrossPartitionReceiverStateV1>(tx, receiverKey)
            ?? throw Errors.Fail(ErrorCode.Conflict, ReceiverNotCommitted);
        GraphCrossPartitionValidation.ValidateReceiver(receiver, sourcePartition, request.Graph,
            request.EdgeId, request.Destination);
        if (receiver.Revision != intent.Revision || receiver.Deleted != intent.Deleted
            || receiver.Fingerprint != intent.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReceiverNotCommitted);
        }
        GraphCrossPartitionCapacityWriter.Apply(tx, sourcePartition,
            GraphCrossPartitionCapacityDirection.PendingIntents,
            GraphCrossPartitionKeys.IntentPrefix(sourcePartition),
            [new(GraphCrossPartitionKeys.Intent(intent), null)], Limits);
        return new(CompleteReceipt, request.Graph, request.EdgeId, intent.Revision);
    }

    internal void RequireGraphWrite(IKeyValueView view, PrincipalRecord principal,
        PartitionRef source, PartitionRef destination, string graph)
    {
        _ = Resource(view, source, graph, ResourceKind.Graph);
        _ = Resource(view, destination, graph, ResourceKind.Graph);
        Authorization.Require(principal, source, graph, Capability.GraphWrite);
        Authorization.Require(principal, destination, graph, Capability.GraphWrite);
    }

    private static GraphCrossPartitionDeliveryIntentV1? CurrentIntent(IKeyValueView view,
        PartitionRef source, string graph, string edgeId, EntityRef destination, DatabaseLimits limits)
    {
        var key = GraphCrossPartitionKeys.Intent(source, graph, edgeId, destination);
        var intent = GraphCrossPartitionRecords.Read<GraphCrossPartitionDeliveryIntentV1>(view, key);
        if (intent is not null)
        {
            GraphCrossPartitionValidation.ValidateIntent(intent, source, graph, edgeId, destination, limits);
        }
        return intent;
    }

    private void VerifyCurrentIntent(GraphCrossPartitionDeliveryIntentV1 intent,
        GraphEdgeOwnerVersionV1 owner, EdgeRecord? canonical)
    {
        var canonicalDeleted = owner.Deleted && canonical is null;
        var canonicalActive = !owner.Deleted && canonical is not null
            && canonical.Revision == owner.Revision;
        if (owner.Revision != intent.Revision || !(canonicalDeleted || canonicalActive)
            || canonical is not null && !CanonicalMatchesOwner(canonical, intent, owner)
            || !IntentMatchesCanonical(intent, owner, canonical)
            || GraphCrossPartitionRecords.Fingerprint(intent.SourcePartition, intent.Graph, intent.EdgeId,
                intent.Destination, intent.Revision, intent.Deleted, intent.Edge) != intent.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, InconsistentIntent);
        }
    }

    private bool CanonicalMatchesOwner(EdgeRecord canonical, GraphCrossPartitionDeliveryIntentV1 intent,
        GraphEdgeOwnerVersionV1 owner)
    {
        GraphCrossPartitionValidation.ValidateEdge(canonical, Limits);
        return canonical.Id == intent.EdgeId && canonical.From.Partition == intent.SourcePartition
            && canonical.Revision == owner.Revision;
    }

    private static bool IntentMatchesCanonical(GraphCrossPartitionDeliveryIntentV1 intent,
        GraphEdgeOwnerVersionV1 owner, EdgeRecord? canonical)
    {
        if (!intent.Deleted)
        {
            return !owner.Deleted && canonical == intent.Edge && canonical?.To == intent.Destination;
        }
        return owner.Deleted && canonical is null || !owner.Deleted
            && canonical is not null && canonical.To != intent.Destination;
    }

    private static MutationReceipt NoChange(string kind, string graph, string edgeId, long revision)
        => new(kind, graph, edgeId, revision);
}
