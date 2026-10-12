using System.Collections.Immutable;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Models;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void PersistRemoteGraphSource(IAtomicTransaction tx, RemoteGraphSourceIdentityV1 original,
        EdgeRecord? previous, EdgeRecord? next, string graph, string edgeId, long revision,
        ImmutableArray<RemoteGraphIntentV1> intents)
    {
        var key = EdgeKey(original.SourcePartition, graph, edgeId);
        if (next is not null)
        { PersistCanonicalEdge(tx, original.SourcePartition, original.Upsert!, key, previous, next); }
        else
        {
            RemoveAdjacency(tx, original.SourcePartition, graph, previous!);
            tx.Delete(key);
        }
        PersistOwnerVersion(tx, original.SourcePartition, graph, edgeId, revision, next is null);
        var writes = intents.Select(intent => new GraphCrossPartitionRecordWrite(
            RemoteGraphKeys.Intent(original.SourcePartition, graph, edgeId, intent.Content.Destination),
            GraphCrossPartitionRecords.Encode(intent, Limits.MaxBatchBytes))).ToArray();
        RemoteGraphCapacityWriter.Apply(tx, original.SourcePartition, RemoteGraphPendingCapacity,
            KeySpace.Partition(RemoteGraphProtocol.IntentFamily, original.SourcePartition), writes, Limits);
    }

    private const string RemoteGraphPendingCapacity = "pending-intents";
}
