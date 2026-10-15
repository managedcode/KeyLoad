using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class HeavyRouteRf3Results
{
    internal static async Task PathAsync(GraphShortestPathResult actual, HeavyRouteRf3State state)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        await SqlRf3Protocol.EqualAsync(new GraphShortestPathResult(1, true, 2,
            [state.Vertex(HeavyRouteRf3State.ItemA), state.Vertex(HeavyRouteRf3State.ItemB), state.Vertex(HeavyRouteRf3State.ItemC)],
            [.. state.Edges], actual.CutPosition), actual);
    }
    internal static async Task IncomingAsync(GraphIncomingEdgesPageV1 actual, HeavyRouteRf3State state)
    {
        await Assert.That(actual.CutPosition).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        await SqlRf3Protocol.EqualAsync(new GraphIncomingEdgesPageV1(1, [new(state.Edges[1], 0L)], actual.CutPosition,
            "eventual-reverse.v1"), actual);
    }
    internal static RankedDocument[] Hits(HeavyRouteRf3State state)
        => [new(new(state.Vertex(HeavyRouteRf3State.ItemA), 1L, "{}", false, []), 1d / 61d),
            new(new(state.Vertex(HeavyRouteRf3State.ItemB), 1L, "{}", false, []), 1d / 62d),
            new(new(state.Vertex(HeavyRouteRf3State.ItemC), 1L, "{}", false, []), 1d / 63d)];
    internal static Task SearchAsync(GraphSearchResult actual, HeavyRouteRf3State state)
        => SqlRf3Protocol.EqualAsync(new GraphSearchResult([.. Hits(state)]), actual);
    internal static async Task AnnAsync(AnnSearchPage actual, HeavyRouteRf3State state)
    {
        await Assert.That(actual.Position).IsGreaterThanOrEqualTo(state.Receipt.Token.Position);
        await SqlRf3Protocol.EqualAsync(new AnnSearchPage(1, [.. Hits(state)], actual.Position, AnnPageMode.Exact, true,
            state.Pin.IndexGeneration, AnnPageMode.Approximate), actual);
    }
}
