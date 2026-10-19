namespace KeyLoad.Server;

internal static class SearchApi
{
    private const string SearchPath = "/search";
    private const string GraphSearchPath = "/search/graph";

    internal static void Map(WebApplication app)
    {
        app.MapPost(AnnMaintenanceProtocol.Route, (AnnMaintenanceRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.MaintainAnnIndex, request.CommandId, request));
        app.MapPost(TextIndexMaintenanceProtocol.Route, (TextIndexMaintenanceRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.MaintainTextIndex, request.CommandId, request));
        app.MapPost(OnlineTextIndexMaintenanceProtocol.Route, (OnlineTextIndexMaintenanceRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.MaintainOnlineTextIndex, request.CommandId, request));
        app.MapPost(AnnSearchProtocol.Route, (ApproximateSearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.ApproximateSearch, request));
        app.MapPost(WaitForAnnIndexProtocol.Route, (WaitForAnnIndexRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.WaitForAnnIndex, request));
        app.MapPost(WaitForIndexProtocol.Route, (HttpContext context, WaitForIndexRequest request) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.WaitForIndex, request));
        app.MapPost(SearchPath, (SearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.Search, request));
        app.MapPost(GraphSearchPath, (GraphSearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.GraphSearch, request));
    }
}
