using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Re-reads and projects only the selected shortest path for bounded output.</summary>
internal sealed class GraphShortestPathResultProjector(DatabaseEngine database, IKeyValueView view,
    PrincipalRecord principal, ResourceDefinition graph, GraphShortestPathRequest request,
    ReadExecutionBudget budget, GraphPathRetention retention)
{
    private const int ContractVersion = 1;
    private const int OneHop = 1;
    private const string EdgeSpace = "edge";
    private const string AdjacencySpace = "adjacency";
    private const string OutDirection = "out";
    private const string MissingEdge = "A graph adjacency record has no canonical edge.";
    private const string InconsistentPredecessor = "The graph path predecessor chain is inconsistent.";

    internal GraphShortestPathResult Found(EntityRef[] path,
        IReadOnlyDictionary<EntityRef, GraphPathPredecessor> predecessors, int hops)
    {
        var envelope = new GraphShortestPathResult(ContractVersion, true, hops,
            ImmutableArray<EntityRef>.Empty, ImmutableArray<EdgeRecord>.Empty, database.Store.Position);
        retention.AdmitResult(budget.MeasureResult(envelope));
        var vertices = ProjectVertices(path);
        var edges = ProjectEdges(path, predecessors);
        var result = envelope with { Vertices = vertices, Edges = edges };
        budget.CheckResult(result);
        return result;
    }

    internal GraphShortestPathResult Missing()
    {
        var result = new GraphShortestPathResult(ContractVersion, false, null, ImmutableArray<EntityRef>.Empty,
            ImmutableArray<EdgeRecord>.Empty, database.Store.Position);
        retention.AdmitResult(budget.MeasureResult(result));
        budget.CheckResult(result);
        return result;
    }

    private ImmutableArray<EntityRef> ProjectVertices(EntityRef[] path)
    {
        var vertices = ImmutableArray.CreateBuilder<EntityRef>(path.Length);
        for (var index = 0; index < path.Length; index++)
        {
            budget.Check();
            var vertex = path[index];
            retention.AdmitResult(ArrayElementBytes(vertex, index));
            vertices.Add(vertex);
        }
        return vertices.MoveToImmutable();
    }

    private ImmutableArray<EdgeRecord> ProjectEdges(EntityRef[] vertices,
        IReadOnlyDictionary<EntityRef, GraphPathPredecessor> predecessors)
    {
        var projected = ImmutableArray.CreateBuilder<EdgeRecord>(vertices.Length - OneHop);
        for (var index = 0; index < vertices.Length - OneHop; index++)
        {
            budget.Check();
            var target = vertices[index + OneHop];
            var predecessor = predecessors[target];
            if (predecessor.Previous != vertices[index])
            {
                throw Errors.Fail(ErrorCode.Corruption, InconsistentPredecessor);
            }
            var edge = budget.ReadRecord<EdgeRecord>(view, KeySpace.Partition(EdgeSpace,
                request.Partition, request.Graph, predecessor.EdgeId))
                ?? throw Errors.Fail(ErrorCode.Corruption, MissingEdge);
            GraphShortestPathValidation.ValidateAdjacency(
                KeySpace.Partition(AdjacencySpace, request.Partition, request.Graph, OutDirection,
                    predecessor.Previous.Collection, predecessor.Previous.Id, predecessor.EdgeId),
                predecessor.EdgeId, edge, predecessor.Previous, request.Partition, request.Graph, budget);
            var visible = edge with
            {
                AttributesJson = database.Authorization.Project(principal, graph.FieldPolicies,
                    edge.AttributesJson, out _)
            };
            retention.AdmitResult(ArrayElementBytes(visible, index));
            projected.Add(visible);
        }
        return projected.MoveToImmutable();
    }

    private long ArrayElementBytes<T>(T value, int index)
        => checked(budget.MeasureResult(value) + (index == 0 ? 0 : OneHop));
}
