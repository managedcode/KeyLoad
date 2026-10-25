using KeyLoad.Core.GraphTraversal.Models;
using KeyLoad.Core.GraphTraversal.Serialization;
using KeyLoad.Core.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.GraphTraversal;

internal static class GraphCrossPartitionIntentWriter
{
    private const string InvalidIntentKey = "A graph delivery intent key is inconsistent.";

    internal static void Persist(DatabaseEngine database, IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionRef source, string graph, string edgeId,
        EdgeRecord? previous, EdgeRecord? next, long nextRevision)
    {
        const int EmptyUpdateCount = 0;

        var desired = ReadPendingTargets(transaction, source, graph, edgeId);
        AddPriorTarget(desired, source, previous, next, nextRevision);
        AddCurrentTarget(desired, source, next);
        var updates = BuildUpdates(database, transaction, principal, source, graph,
            desired, next, nextRevision);
        if (updates.Count > EmptyUpdateCount)
        {
            GraphCrossPartitionCapacityWriter.Apply(transaction, source,
                GraphCrossPartitionCapacityDirection.PendingIntents,
                GraphCrossPartitionKeys.IntentPrefix(source), updates);
        }
    }

    private static Dictionary<EntityRef, EdgeRecord> ReadPendingTargets(IAtomicTransaction transaction,
        PartitionRef source, string graph, string edgeId)
    {
        var targets = new Dictionary<EntityRef, EdgeRecord>();
        var prefix = GraphCrossPartitionKeys.IntentEdgePrefix(source, graph, edgeId);
        _ = transaction.VisitAll(prefix, (key, bytes) =>
        {
            var intent = GraphCrossPartitionRecords.Decode<GraphCrossPartitionDeliveryIntent>(bytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, InvalidIntentKey);
            GraphCrossPartitionValidation.ValidateIntent(intent, source, graph, edgeId,
                intent.Destination);
            if (!key.SequenceEqual(GraphCrossPartitionKeys.Intent(intent)))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidIntentKey);
            }
            targets[intent.Destination] = intent.Edge;
            return true;
        });
        return targets;
    }

    private static void AddPriorTarget(Dictionary<EntityRef, EdgeRecord> desired,
        PartitionRef source, EdgeRecord? previous, EdgeRecord? next, long revision)
    {
        if (previous is { } oldEdge && oldEdge.To.Partition != source
            && (next is null || next.To != oldEdge.To))
        {
            desired[oldEdge.To] = oldEdge with
            {
                Revision = revision
            };
        }
    }

    private static void AddCurrentTarget(Dictionary<EntityRef, EdgeRecord> desired,
        PartitionRef source, EdgeRecord? next)
    {
        if (next is { } newEdge && newEdge.To.Partition != source)
        {
            desired[newEdge.To] = newEdge;
        }
    }

    private static List<GraphCrossPartitionRecordWrite> BuildUpdates(DatabaseEngine database,
        IAtomicTransaction transaction, PrincipalRecord principal, PartitionRef source, string graph,
        Dictionary<EntityRef, EdgeRecord> desired, EdgeRecord? next, long revision)
    {
        var updates = new List<GraphCrossPartitionRecordWrite>(desired.Count);
        foreach (var (target, snapshot) in desired.OrderBy(pair => pair.Key.Partition.TenantId,
                     StringComparer.Ordinal).ThenBy(pair => pair.Key.Partition.DatabaseId, StringComparer.Ordinal)
                 .ThenBy(pair => pair.Key.Partition.TransactionDomainId, StringComparer.Ordinal)
                 .ThenBy(pair => pair.Key.Partition.PartitionKey, StringComparer.Ordinal)
                 .ThenBy(pair => pair.Key.Collection, StringComparer.Ordinal)
                 .ThenBy(pair => pair.Key.Id, StringComparer.Ordinal))
        {
            database.RequireGraphWrite(transaction, principal, source, target.Partition, graph);
            var deleted = next is null || target != next.To;
            var edge = deleted ? snapshot with
            {
                Revision = revision
            } : next!;
            AddUpdate(updates, principal, source, graph, edge, target, deleted);
        }
        return updates;
    }

    private static void AddUpdate(List<GraphCrossPartitionRecordWrite> updates,
        PrincipalRecord principal, PartitionRef source, string graph, EdgeRecord edge,
        EntityRef target, bool deleted)
    {
        const int FirstTargetIndex = 0;

        var fingerprint = GraphCrossPartitionRecords.Fingerprint(source, graph, edge.Id,
            target, edge.Revision, deleted, edge);
        var intent = new GraphCrossPartitionDeliveryIntent(GraphCrossPartitionProtocol.CurrentVersion,
            source, graph, edge.Id, target, edge.Revision, deleted, edge, principal.Id,
            principal.PolicyEpoch, fingerprint);
        var key = GraphCrossPartitionKeys.Intent(intent);
        var value = GraphCrossPartitionRecords.Encode(intent);
        var index = updates.FindIndex(write => write.Key.AsSpan().SequenceEqual(key));
        var replacement = new GraphCrossPartitionRecordWrite(key, value);
        if (index < FirstTargetIndex)
        {
            updates.Add(replacement);
        }
        else
        {
            updates[index] = replacement;
        }
    }
}
