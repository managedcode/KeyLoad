using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.InternalSerialization;

namespace KeyLoad.UnitTests.GraphTraversal;

internal static class GraphTraversalIntegrityFaultRows
{
    private const string EdgeSpace = "edge";
    private const string AdjacencySpace = "adjacency";
    private const string OutDirection = "out";
    private const string ForeignEdgeId = "foreign-edge";
    private const string NoncanonicalAttributes = "{ \"value\" : 1 }";
    private const long InvalidRevision = 0;
    private const int RuntimeWrongType = 7;
    private const byte MalformedByte = byte.MaxValue;

    internal static GraphTraversalOwnedFault Install(ZoneTreeStore store, GraphTraversalIntegrityState state,
        GraphTraversalIntegrityFault fault)
    {
        var adjacency = fault is GraphTraversalIntegrityFault.AdjacencyIdentity
            or GraphTraversalIntegrityFault.MalformedAdjacency or GraphTraversalIntegrityFault.WrongAdjacencyType;
        var edge = state.DamagedEdge;
        var key = adjacency ? KeySpace.Partition(AdjacencySpace, edge.From.Partition, GraphTraversalIntegrityFixture.Graph,
            OutDirection, edge.From.Collection, edge.From.Id, edge.Id)
            : KeySpace.Partition(EdgeSpace, edge.From.Partition, GraphTraversalIntegrityFixture.Graph, edge.Id);
        var original = store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The graph fixture must own the original fault row.");
        store.Commit((transaction, _) =>
        {
            if (fault == GraphTraversalIntegrityFault.MissingEdge)
            {
                transaction.Delete(key);
            }
            else if (fault == GraphTraversalIntegrityFault.NullTarget)
            {
                transaction.Put(key, GraphFixtures.EncodeUnchecked(edge with
                {
                    To = null!
                }));
            }
            else if (adjacency)
            {
                transaction.Put(key, AdjacencyBytes(state, fault));
            }
            else
            {
                transaction.PutRecord(key, DamagedEdge(edge, fault));
            }
            return true;
        });
        return new(key, original);
    }

    internal static void Repair(ZoneTreeStore store, GraphTraversalOwnedFault fault)
        => store.Commit((transaction, _) => { transaction.Put(fault.Key, fault.Original); return true; });

    private static byte[] AdjacencyBytes(GraphTraversalIntegrityState state, GraphTraversalIntegrityFault fault)
        => fault switch
        {
            GraphTraversalIntegrityFault.AdjacencyIdentity => OrleansSerialization.Serialize(state.HealthyEdge.Id),
            GraphTraversalIntegrityFault.MalformedAdjacency => [MalformedByte],
            GraphTraversalIntegrityFault.WrongAdjacencyType => OrleansSerialization.Serialize(RuntimeWrongType),
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };

    private static EdgeRecord DamagedEdge(EdgeRecord edge, GraphTraversalIntegrityFault fault)
        => fault switch
        {
            GraphTraversalIntegrityFault.EdgeIdentity => edge with { Id = ForeignEdgeId },
            GraphTraversalIntegrityFault.EdgeSource => edge with { From = edge.To },
            GraphTraversalIntegrityFault.InvalidLabel => edge with { Label = string.Empty },
            GraphTraversalIntegrityFault.InvalidTargetId => edge with { To = edge.To with { Id = string.Empty } },
            GraphTraversalIntegrityFault.InvalidAttributes => edge with { AttributesJson = NoncanonicalAttributes },
            GraphTraversalIntegrityFault.NonpositiveRevision => edge with { Revision = InvalidRevision },
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
}
