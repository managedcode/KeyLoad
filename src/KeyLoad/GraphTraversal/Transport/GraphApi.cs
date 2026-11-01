namespace KeyLoad.Server;

internal static class GraphApi
{
    private const string TraversePath = ApiRoutes.GraphTraverse;
    internal static void Map(WebApplication app)
    {
        app.MapPost(TraversePath, (TraverseRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.Traverse, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(McpToolRoutes.GraphShortestPath, (GraphShortestPathRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.GraphShortestPath, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(McpToolRoutes.GraphIncomingEdges, (ReadIncomingGraphEdgesRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.GraphIncomingEdges, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
    }
}
