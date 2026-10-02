namespace KeyLoad.Server;

internal static class SearchApi
{
    private const string SearchPath = "/v1/search";

    internal static void Map(WebApplication app) => app.MapPost(SearchPath, (SearchRequest request, HttpContext context) =>
        ApiGrainDispatch.ReadAsync(context, KeyLoad.Orleans.GrainReadKind.Search, request));
}
