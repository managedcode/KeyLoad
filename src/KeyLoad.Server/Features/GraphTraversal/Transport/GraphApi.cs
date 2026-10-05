namespace KeyLoad.Server;

internal static class GraphApi
{
    private const string TraversePath = "/v1/graph/traverse";
    internal static void Map(WebApplication app)
    {
        app.MapPost(TraversePath, (TraverseRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.Traverse, request));
        app.MapPost(McpToolRoutes.GraphShortestPath, (GraphShortestPathRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.GraphShortestPath, request));
    }
}
