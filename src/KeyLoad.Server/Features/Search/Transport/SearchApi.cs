namespace KeyLoad.Server;

internal static class SearchApi
{
    private const string SearchPath = "/v1/search";
    private const string GraphSearchPath = "/v1/search/graph";

    internal static void Map(WebApplication app)
    {
        app.MapPost(WaitForIndexProtocol.Route, (HttpContext context, WaitForIndexRequest request) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.WaitForIndex, request));
        app.MapPost(SearchPath, (SearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.Search, request));
        app.MapPost(GraphSearchPath, (GraphSearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.GraphSearch, request));
    }
}
