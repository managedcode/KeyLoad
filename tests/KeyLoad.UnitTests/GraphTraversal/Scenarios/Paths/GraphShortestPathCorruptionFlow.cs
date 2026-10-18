using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal static class GraphShortestPathCorruptionFlow
{
    internal const string Nodes = "corruption-path-nodes";
    private const string Source = "source";
    private const string Target = "target";
    private const string EdgeId = "canonical-edge";
    private const string WrongEdgeId = "wrong-edge";
    private const string OtherPartitionKey = "other-partition";
    private const string Attributes = "{\"purpose\":\"exact-corruption-repair\"}";
    private const string EdgeSpace = "edge";
    private const string AdjacencySpace = "adjacency";
    private const string OutDirection = "out";
    private const int ContractVersion = 1;
    private const int OneHop = 1;
    private const int TwoVertices = 2;
    private const int WrongAdjacencyValue = 7;
    private const byte InvalidNativeByte = byte.MaxValue;
    private const long InitialRevision = 1;

    internal static EdgeRecord Seed(TestDatabase database)
    {
        var from = new EntityRef(database.Partition, Nodes, Source);
        var to = new EntityRef(database.Partition, Nodes, Target);
        database.Commit(new PutDocument(Nodes, Source, GraphShortestPathTestSupport.EmptyJson),
            new PutDocument(Nodes, Target, GraphShortestPathTestSupport.EmptyJson),
            new UpsertEdge(GraphShortestPathTestSupport.Graph, EdgeId, from, to,
                GraphShortestPathTestSupport.Walk, Attributes));
        return new(EdgeId, from, to, GraphShortestPathTestSupport.Walk, Attributes, InitialRevision);
    }

    internal static GraphShortestPathRequest Request(EdgeRecord expected)
        => new(ContractVersion, expected.From.Partition, GraphShortestPathTestSupport.Graph,
            expected.From, expected.To, OneHop, TwoVertices, OneHop);

    internal static void Inject(ZoneTreeStore store, EdgeRecord expected, GraphShortestPathCorruptionCase testCase)
    {
        store.Commit((transaction, _) =>
        {
            if (testCase == GraphShortestPathCorruptionCase.MissingEdge)
            {
                transaction.Delete(EdgeKey(expected));
            }
            else if (testCase == GraphShortestPathCorruptionCase.MalformedAdjacency)
            {
                transaction.Put(AdjacencyKey(expected), [InvalidNativeByte]);
            }
            else if (testCase == GraphShortestPathCorruptionCase.WrongAdjacencyType)
            {
                transaction.Put(AdjacencyKey(expected), NativeSerialization.Serialize(WrongAdjacencyValue));
            }
            else
            {
                transaction.PutRecord(EdgeKey(expected), DamagedEdge(expected, testCase));
            }
            return true;
        });
    }

    private static EdgeRecord DamagedEdge(EdgeRecord expected, GraphShortestPathCorruptionCase testCase)
        => testCase switch
        {
            GraphShortestPathCorruptionCase.EdgeIdentity => expected with { Id = WrongEdgeId },
            GraphShortestPathCorruptionCase.EdgeSource => expected with { From = expected.To },
            GraphShortestPathCorruptionCase.ForeignTarget => expected with
            {
                To = expected.To with
                {
                    Partition = expected.To.Partition with
                    {
                        PartitionKey = OtherPartitionKey
                    }
                }
            },
            GraphShortestPathCorruptionCase.InvalidLabel => expected with { Label = string.Empty },
            GraphShortestPathCorruptionCase.InvalidTargetId => expected with { To = expected.To with { Id = string.Empty } },
            _ => throw new ArgumentOutOfRangeException(nameof(testCase))
        };

    internal static void Repair(ZoneTreeStore store, EdgeRecord expected, GraphShortestPathCorruptionCase testCase)
    {
        store.Commit((transaction, _) =>
        {
            if (testCase is GraphShortestPathCorruptionCase.MalformedAdjacency or GraphShortestPathCorruptionCase.WrongAdjacencyType)
            {
                transaction.PutRecord(AdjacencyKey(expected), expected.Id);
            }
            else
            {
                transaction.PutRecord(EdgeKey(expected), expected);
            }
            return true;
        });
    }

    internal static async Task RefuseAsync(DatabaseEngine owner, ZoneTreeStore store,
        GraphShortestPathRequest request, CancellationToken cancellationToken)
    {
        var image = CanonicalStoreImage.Bytes(store);
        var position = store.Position;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => owner.ShortestPath(
            GraphShortestPathTestSupport.RootPrincipal, request, cancellationToken: cancellationToken));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image)).IsTrue();
    }

    internal static async Task HealthyAsync(DatabaseEngine owner, ZoneTreeStore store,
        EdgeRecord expected, CancellationToken cancellationToken)
    {
        var position = store.Position;
        var image = CanonicalStoreImage.Bytes(store);
        var actual = owner.ShortestPath(GraphShortestPathTestSupport.RootPrincipal, Request(expected),
            cancellationToken: cancellationToken);
        await Assert.That(actual.Version).IsEqualTo(ContractVersion);
        await Assert.That(actual.Found).IsTrue();
        await Assert.That(actual.Hops).IsEqualTo(OneHop);
        await Assert.That(actual.Vertices.SequenceEqual([expected.From, expected.To])).IsTrue();
        await Assert.That(actual.Edges.SequenceEqual([expected])).IsTrue();
        await Assert.That(actual.CutPosition).IsEqualTo(position);
        await Assert.That(store.Position).IsEqualTo(position);
        await Assert.That(CanonicalStoreImage.Bytes(store).SequenceEqual(image)).IsTrue();
    }

    private static byte[] EdgeKey(EdgeRecord edge)
        => KeySpace.Partition(EdgeSpace, edge.From.Partition, GraphShortestPathTestSupport.Graph, edge.Id);

    private static byte[] AdjacencyKey(EdgeRecord edge)
        => KeySpace.Partition(AdjacencySpace, edge.From.Partition, GraphShortestPathTestSupport.Graph,
            OutDirection, edge.From.Collection, edge.From.Id, edge.Id);
}
