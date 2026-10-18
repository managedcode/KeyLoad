namespace KeyLoad.Server;

internal static class GraphApi
{
    private const string TraversePath = "/v1/graph/traverse";
    internal static void Map(WebApplication app)
    {
        app.MapPost(TraversePath, (TraverseRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.Traverse, request));
        app.MapPost(McpToolRoutes.GraphShortestPath, (GraphShortestPathRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.GraphShortestPath, request));
        app.MapPost(McpToolRoutes.GraphIncomingEdges, (ReadIncomingGraphEdgesRequestV1 request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.GraphIncomingEdges, request));
    }
}
