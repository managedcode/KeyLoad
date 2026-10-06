using KeyLoad.Core.Features.GraphTraversal.Models;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal;

internal static class GraphCrossPartitionIntentWriter
{
    private const string IntentScanExceeded = "The pending graph delivery intent scan is exhausted.";
    private const string IntentBytesExceeded = "The pending graph delivery intent byte budget is exhausted.";
    private const string InvalidIntentKey = "A graph delivery intent key is inconsistent.";

    internal static void Persist(DatabaseEngine database, IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionRef source, string graph, string edgeId,
        EdgeRecord? previous, EdgeRecord? next, long nextRevision, DatabaseLimits limits)
    {
        const int EmptyUpdateCount = 0;

        var desired = ReadPendingTargets(transaction, source, graph, edgeId, limits);
        AddPriorTarget(desired, source, previous, next, nextRevision);
        AddCurrentTarget(desired, source, next);
        var updates = BuildUpdates(database, transaction, principal, source, graph,
            desired, next, nextRevision, limits);
        if (updates.Count > EmptyUpdateCount)
        {
            GraphCrossPartitionCapacityWriter.Apply(transaction, source,
                GraphCrossPartitionCapacityDirection.PendingIntents,
                GraphCrossPartitionKeys.IntentPrefix(source), updates, limits);
        }
    }

    private static Dictionary<EntityRef, EdgeRecord> ReadPendingTargets(IAtomicTransaction transaction,
        PartitionRef source, string graph, string edgeId, DatabaseLimits limits)
    {
        const int NoEncodedBytes = 0;

        var targets = new Dictionary<EntityRef, EdgeRecord>();
        var prefix = GraphCrossPartitionKeys.IntentEdgePrefix(source, graph, edgeId);
        long encodedBytes = NoEncodedBytes;
        var scan = transaction.VisitRange(prefix, limits.MaxScanRecords, (key, bytes) =>
        {
            AcceptRowBytes(key, bytes, limits.MaxBatchBytes, ref encodedBytes);
            var intent = GraphCrossPartitionRecords.Decode<GraphCrossPartitionDeliveryIntentV1>(bytes)
                ?? throw Errors.Fail(ErrorCode.Corruption, InvalidIntentKey);
            GraphCrossPartitionValidation.ValidateIntent(intent, source, graph, edgeId,
                intent.Destination, limits);
            if (!key.SequenceEqual(GraphCrossPartitionKeys.Intent(intent)))
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidIntentKey);
            }
            targets[intent.Destination] = intent.Edge;
            return true;
        });
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, IntentScanExceeded);
        }
        return targets;
    }

    private static void AcceptRowBytes(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value,
        long maximumBytes, ref long encodedBytes)
    {
        var rowBytes = checked((long)value.Length + key.Length);
        if (rowBytes > maximumBytes - encodedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, IntentBytesExceeded);
        }
        encodedBytes = checked(encodedBytes + rowBytes);
    }

    private static void AddPriorTarget(Dictionary<EntityRef, EdgeRecord> desired,
        PartitionRef source, EdgeRecord? previous, EdgeRecord? next, long revision)
    {
        if (previous is { } oldEdge && oldEdge.To.Partition != source
            && (next is null || next.To != oldEdge.To))
        {
            desired[oldEdge.To] = oldEdge with { Revision = revision };
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
        Dictionary<EntityRef, EdgeRecord> desired, EdgeRecord? next, long revision, DatabaseLimits limits)
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
            DatabaseEngine.RequireSameGraphOwner(transaction, source, target.Partition);
            var deleted = next is null || target != next.To;
            var edge = deleted ? snapshot with { Revision = revision } : next!;
            AddUpdate(updates, principal, source, graph, edge, target, deleted, limits);
        }
        return updates;
    }

    private static void AddUpdate(List<GraphCrossPartitionRecordWrite> updates,
        PrincipalRecord principal, PartitionRef source, string graph, EdgeRecord edge,
        EntityRef target, bool deleted, DatabaseLimits limits)
    {
        const int FirstTargetIndex = 0;

        var fingerprint = GraphCrossPartitionRecords.Fingerprint(source, graph, edge.Id,
            target, edge.Revision, deleted, edge);
        var intent = new GraphCrossPartitionDeliveryIntentV1(GraphCrossPartitionProtocol.CurrentVersion,
            source, graph, edge.Id, target, edge.Revision, deleted, edge, principal.Id,
            principal.PolicyEpoch, fingerprint);
        var key = GraphCrossPartitionKeys.Intent(intent);
        var value = GraphCrossPartitionRecords.Encode(intent, limits.MaxBatchBytes);
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
