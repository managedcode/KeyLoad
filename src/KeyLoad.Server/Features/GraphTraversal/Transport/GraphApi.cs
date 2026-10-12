namespace KeyLoad.Server;

internal static class GraphApi
{
    private const string TraversePath = "/v1/graph/traverse";
    internal static void Map(WebApplication app)
    {
        app.MapPost(RemoteGraphPublicProtocol.UpsertRoute, (UpsertRemoteGraphEdgeRequestV1 request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.UpsertRemoteGraphEdge, request.CommandId, request));
        app.MapPost(RemoteGraphPublicProtocol.DeleteRoute, (DeleteRemoteGraphEdgeRequestV1 request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.DeleteRemoteGraphEdge, request.CommandId, request));
        app.MapPost(RemoteGraphPublicProtocol.DeliverRoute, (DeliverRemoteGraphEdgeRequestV1 request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.DeliverRemoteGraphEdge, request.CommandId, request));
        app.MapPost(RemoteGraphPublicProtocol.IncomingRoute, (ReadRemoteIncomingGraphEdgesRequestV1 request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.RemoteGraphIncomingParent, request));
        app.MapPost(TraversePath, (TraverseRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.Traverse, request));
        app.MapPost(McpToolRoutes.GraphShortestPath, (GraphShortestPathRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.GraphShortestPath, request));
        app.MapPost(McpToolRoutes.GraphIncomingEdges, (ReadIncomingGraphEdgesRequestV1 request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.GraphIncomingEdges, request));
    }
}
