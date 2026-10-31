using KeyLoad.ComparisonHost.Measurement;

namespace KeyLoad.ComparisonHost.Graph;

/// <summary>The graph scenario, shared by every graph database and KeyLoad.</summary>
internal static class GraphScenario
{
    internal const string Load = "load";
    internal const string Neighbors = "neighbors";
    internal const string Traverse = "traverse";
    private const string WrongNeighbors = "Wrong neighbours of ";
    private const string WrongReach = "Wrong reachable count from ";

    /// <summary>Loads all vertices, then all edges; operations count both.</summary>
    internal static async Task<CaseResult> LoadAsync(IGraphDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        var graph = new GraphDataset(settings.Records);
        if (graph.Vertices <= GraphDataset.FanOut + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Records, null);
        }
        await driver.PrepareAsync(token);
        var vertices = await OperationLanes.RunBatchesAsync(Load, graph.Vertices, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.InsertVerticesAsync(
                [.. Enumerable.Range(first, count).Select(GraphDataset.Vertex)], cancellation), token);
        var edges = await OperationLanes.RunBatchesAsync(Load, graph.Edges, driver.BatchSize, settings.Clients,
            (first, count, cancellation) => driver.InsertEdgesAsync(
                [.. Enumerable.Range(first, count).Select(graph.Edge)], cancellation), token);
        return vertices.Then(edges);
    }

    internal static Task<CaseResult> NeighborsAsync(IGraphDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        var graph = new GraphDataset(settings.Records);
        return OperationLanes.RunAsync(Neighbors, settings.Operations, settings.Clients, settings.CaseLimit, async (index, cancellation) =>
        {
            var vertex = settings.Spread(index, 0, graph.Vertices);
            var found = await driver.NeighborsAsync(GraphDataset.VertexId(vertex), cancellation);
            if (found.Count != GraphDataset.FanOut
                || !found.ToHashSet(StringComparer.Ordinal).SetEquals(graph.Neighbors(vertex).Select(GraphDataset.VertexId)))
            {
                throw new InvalidDataException(WrongNeighbors + GraphDataset.VertexId(vertex));
            }
        }, token);
    }

    internal static Task<CaseResult> TraverseAsync(IGraphDriver driver, BenchmarkSettings settings, CancellationToken token)
    {
        var graph = new GraphDataset(settings.Records);
        return OperationLanes.RunAsync(Traverse, settings.Operations, settings.Clients, settings.CaseLimit, async (index, cancellation) =>
        {
            var vertex = settings.Spread(index, 0, graph.Vertices);
            var count = await driver.CountReachableAsync(GraphDataset.VertexId(vertex), GraphDataset.TraversalDepth, cancellation);
            if (count != graph.Reachable(vertex, GraphDataset.TraversalDepth))
            {
                throw new InvalidDataException(WrongReach + GraphDataset.VertexId(vertex));
            }
        }, token);
    }
}
