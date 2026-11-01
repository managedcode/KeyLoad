namespace KeyLoad.Server;

internal static class SearchApi
{
    private const string SearchPath = ApiRoutes.Search;
    private const string GraphSearchPath = "/search/graph";

    internal static void Map(WebApplication app)
    {
        app.MapPost(AnnMaintenanceProtocol.Route, (AnnMaintenanceRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.MaintainAnnIndex, request.CommandId, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(TextIndexMaintenanceProtocol.Route, (TextIndexMaintenanceRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.MaintainTextIndex, request.CommandId, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(OnlineTextIndexMaintenanceProtocol.Route, (OnlineTextIndexMaintenanceRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.MaintainOnlineTextIndex, request.CommandId, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(AnnSearchProtocol.Route, (ApproximateSearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.ApproximateSearch, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(WaitForAnnIndexProtocol.Route, (WaitForAnnIndexRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.WaitForAnnIndex, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(WaitForIndexProtocol.Route, (HttpContext context, WaitForIndexRequest request) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.WaitForIndex, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(SearchPath, (SearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.Search, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(GraphSearchPath, (GraphSearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Clustering.GrainReadKind.GraphSearch, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
    }
}
