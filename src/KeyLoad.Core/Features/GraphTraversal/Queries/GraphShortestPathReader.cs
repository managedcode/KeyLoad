using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Runs explicit bounded breadth-first depth frontiers over native adjacency ranges.</summary>
internal sealed class GraphShortestPathReader
{
    private const int ZeroHops = 0;
    private const int OneHop = 1;
    private const string EdgeSpace = "edge";
    private const string AdjacencySpace = "adjacency";
    private const string OutDirection = "out";
    private const string EdgeBudgetExceeded = "The graph path edge budget was exhausted.";
    private const string VertexBudgetExceeded = "The graph path vertex budget was exhausted.";
    private const string MissingEdge = "A graph adjacency record has no canonical edge.";
    private const string InconsistentVisitor = "The graph path adjacency visitor stopped inconsistently.";
    private readonly IKeyValueView view;
    private readonly GraphShortestPathRequest request;
    private readonly ReadExecutionBudget budget;
    private readonly GraphPathRetention retention;
    private readonly GraphShortestPathResultProjector projector;
    private readonly HashSet<EntityRef> visited = [];
    private readonly Dictionary<EntityRef, GraphPathPredecessor> predecessors = [];
    private readonly GraphVertexVisibility visibility;
    private readonly HashSet<string>? labelSet;
    private EntityRef[]? foundVertices;
    private int examinedEdges;

    internal GraphShortestPathReader(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition graph, GraphShortestPathRequest request, string[]? labels, ReadExecutionBudget budget)
    {
        const int EmptyLabelCount = 0;

        this.view = view;
        this.request = request;
        this.budget = budget;
        retention = new(database.OperationLimitsOptions, budget);
        visibility = new(database, view, principal, budget, retention.AdmitVertex, retention.AdmitCollection);
        projector = new(database, view, principal, graph, request, budget, retention);
        labelSet = labels is { Length: > EmptyLabelCount } ? new(labels, StringComparer.Ordinal) : null;
    }

    internal GraphShortestPathResult Read()
    {
        const int EmptyFrontierCount = 0;

        visibility.RequireStart(request.From);
        visited.Add(request.From);
        if (request.From == request.To)
        {
            foundVertices = [request.From];
            return FoundResult(ZeroHops);
        }
        var frontier = new List<EntityRef> { request.From };
        for (var depth = ZeroHops; depth < request.MaxDepth && frontier.Count > EmptyFrontierCount; depth++)
        {
            budget.Check();
            var next = new List<EntityRef>();
            foreach (var vertex in frontier)
            {
                budget.Check();
                VisitAdjacency(vertex, depth, next);
                if (foundVertices is not null)
                {
                    return FoundResult(checked(depth + OneHop));
                }
            }
            frontier = next;
        }
        return MissingResult();
    }

    private void VisitAdjacency(EntityRef source, int depth, List<EntityRef> next)
    {
        var prefix = KeySpace.Partition(AdjacencySpace, request.Partition, request.Graph,
            OutDirection, source.Collection, source.Id);
        var maxRecords = checked(request.MaxEdges - examinedEdges + OneHop);
        var stoppedAtTarget = false;
        var scan = budget.VisitRange(view, prefix, maxRecords, (key, value) =>
        {
            budget.Check();
            stoppedAtTarget = VisitEdge(key, value, source, depth, next);
            return !stoppedAtTarget;
        });
        if (scan.StoppedByVisitor != stoppedAtTarget)
        {
            throw Errors.Fail(ErrorCode.Corruption, InconsistentVisitor);
        }
        if (stoppedAtTarget)
        {
            return;
        }
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, EdgeBudgetExceeded);
        }
    }

    private bool VisitEdge(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, EntityRef source, int depth,
        List<EntityRef> next)
    {
        const int ExaminedEdgeIncrement = 1;

        examinedEdges = checked(examinedEdges + ExaminedEdgeIncrement);
        if (examinedEdges > request.MaxEdges)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, EdgeBudgetExceeded);
        }
        var edgeId = NativeSerialization.Deserialize<string>(value)
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingEdge);
        var edge = budget.ReadRecord<EdgeRecord>(view, KeySpace.Partition(EdgeSpace,
            request.Partition, request.Graph, edgeId));
        GraphShortestPathValidation.ValidateAdjacency(key, edgeId, edge, source,
            request.Partition, request.Graph, budget);
        var validEdge = edge ?? throw Errors.Fail(ErrorCode.Corruption, MissingEdge);
        if (labelSet is not null && !labelSet.Contains(validEdge.Label))
        {
            return false;
        }
        if (visited.Contains(validEdge.To) || !visibility.CanVisit(validEdge.To))
        {
            return false;
        }
        if (visited.Count >= request.MaxVertices)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, VertexBudgetExceeded);
        }
        var predecessor = new GraphPathPredecessor(source, edgeId);
        retention.AdmitPredecessor(validEdge.To, predecessor);
        visited.Add(validEdge.To);
        predecessors.Add(validEdge.To, predecessor);
        next.Add(validEdge.To);
        if (validEdge.To == request.To)
        {
            foundVertices = ReconstructVertices(validEdge.To, checked(depth + OneHop));
            return true;
        }
        return false;
    }

    private EntityRef[] ReconstructVertices(EntityRef target, int hops)
    {
        const int StartVertexIndex = 0;

        var path = new EntityRef[checked(hops + OneHop)];
        var current = target;
        for (var index = hops; index > ZeroHops; index--)
        {
            budget.Check();
            path[index] = current;
            current = predecessors[current].Previous;
        }
        path[StartVertexIndex] = current;
        return path;
    }

    private GraphShortestPathResult FoundResult(int hops)
        => projector.Found(foundVertices!, predecessors, hops);

    private GraphShortestPathResult MissingResult()
        => projector.Missing();
}
