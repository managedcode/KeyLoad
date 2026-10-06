using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Reads bounded visible reachability without retaining edge or document payloads.</summary>
internal sealed class GraphSearchReachabilityReader(
    DatabaseEngine database,
    IKeyValueView view,
    PrincipalRecord principal,
    PartitionRef partition,
    GraphWalkSpec walk,
    ReadExecutionBudget budget)
{
    private const string AdjacencySpace = "adjacency";
    private const string EdgeSpace = "edge";
    private const string OutDirection = "out";
    private const string MissingEdge = "A graph adjacency record has no edge.";
    private const string MismatchedEdge = "A graph adjacency record does not match its canonical edge.";
    private const string MissingGraph = "The graph resource is unavailable.";
    private const string InvalidGraphResource = "The graph resource is invalid for this partition.";
    private const string LabelField = "/label";
    private const string EdgeLimit = "The graph edge visit budget was exhausted.";
    private const string VertexLimit = "The graph vertex visit budget was exhausted.";
    private const string RetainedLimit = "The graph reachability byte budget was exhausted.";
    private readonly GraphVertexVisibility visibility = new(database, view, principal, budget);
    private readonly Dictionary<EntityRef, int> shortestHops = [];
    private readonly Queue<GraphSearchReachability> pending = new();
    private readonly HashSet<string>? labelSet = walk.Labels is { } labels ? new(labels, StringComparer.Ordinal) : null;
    private long retainedBytes;
    private int examinedEdges;

    internal GraphSearchReachability[] Read()
    {
        database.Authorization.Require(principal, partition, walk.Graph, Capability.GraphRead);
        var graph = budget.ReadRecord<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, walk.Graph))
            ?? throw Errors.Fail(ErrorCode.NotFound, MissingGraph);
        if (graph.Kind != ResourceKind.Graph || graph.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidGraphResource);
        }
        if (walk.Labels is not null)
        {
            database.Authorization.RequireFieldUse(principal, graph, LabelField);
        }
        AddSeeds();
        while (pending.TryDequeue(out var entry))
        {
            budget.Check();
            if (entry.ShortestHops < walk.MaxDepth)
            {
                VisitAdjacency(entry);
            }
        }
        return ToOrderedArray();
    }

    private void AddSeeds()
    {
        const int SeedDepth = 0;

        foreach (var seed in walk.Seeds)
        {
            budget.Check();
            if (shortestHops.ContainsKey(seed))
            {
                continue;
            }
            visibility.RequireStart(seed);
            AddVertex(seed, SeedDepth);
        }
    }

    private void VisitAdjacency(GraphSearchReachability current)
    {
        const int OverflowProbeRows = 1;

        var prefix = KeySpace.Partition(AdjacencySpace, partition, walk.Graph, OutDirection,
            current.Reference.Collection, current.Reference.Id);
        var remaining = Math.Min(walk.MaxEdges, walk.MaxEdges - examinedEdges + OverflowProbeRows);
        var scan = budget.VisitRange(view, prefix, remaining, (_, value) => VisitEdge(value, current));
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, EdgeLimit);
        }
    }

    private bool VisitEdge(ReadOnlySpan<byte> value, GraphSearchReachability current)
    {
        const int HopIncrement = 1;

        budget.Check();
        if (++examinedEdges > walk.MaxEdges)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, EdgeLimit);
        }
        var edgeId = NativeSerialization.Deserialize<string>(value);
        var edge = budget.ReadRecord<EdgeRecord>(view, KeySpace.Partition(EdgeSpace, partition, walk.Graph, edgeId))
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingEdge);
        if (edge.Id != edgeId || edge.From != current.Reference || edge.To.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Corruption, MismatchedEdge);
        }
        if (labelSet is not null && !labelSet.Contains(edge.Label))
        {
            return true;
        }
        if (visibility.CanVisit(edge.To))
        {
            AddVertex(edge.To, checked(current.ShortestHops + HopIncrement));
        }
        return true;
    }

    private void AddVertex(EntityRef reference, int depth)
    {
        if (shortestHops.ContainsKey(reference))
        {
            return;
        }
        if (shortestHops.Count >= walk.MaxVertices)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, VertexLimit);
        }
        var entry = new GraphSearchReachability(reference, depth);
        var bytes = budget.MeasureResult(entry);
        if (bytes > database.Limits.MaxBatchBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RetainedLimit);
        }
        retainedBytes += bytes;
        shortestHops.Add(reference, depth);
        pending.Enqueue(entry);
    }

    private GraphSearchReachability[] ToOrderedArray()
    {
        const int FirstResultIndex = 0;

        var entries = new GraphSearchReachability[shortestHops.Count];
        var index = FirstResultIndex;
        foreach (var (reference, depth) in shortestHops)
        {
            budget.Check();
            entries[index++] = new(reference, depth);
        }
        var comparer = new GraphSearchReachabilityOrderComparer(budget);
        try
        {
            Array.Sort(entries, comparer);
        }
        catch (InvalidOperationException exception)
        {
            comparer.RethrowOwnedBudgetFailure(exception);
            throw;
        }
        budget.Check();
        return entries;
    }
}
