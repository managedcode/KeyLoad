using KeyLoad.Orleans;
using KeyLoad.Query;

namespace KeyLoad.Server;

internal static class QueryApi
{
    private const string QueryPath = "/v1/query";
    private const string AstPath = "/v1/query/ast";
    private const string CapabilitiesPath = "/v1/query/capabilities";
    private const string LiveStartPath = "/v1/query/live/start";
    private const string LiveReadPath = "/v1/query/live/read";

    internal static void Map(WebApplication app)
    {
        app.MapPost(QueryPath, (QueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.Query, request));
        app.MapPost(AstPath, (AstQueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AstQuery, request));
        app.MapGet(CapabilitiesPath, (Func<HttpContext, Task<IResult>>)(context =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.QueryCapabilities)));
        app.MapPost(LiveStartPath, (StartLiveQueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.LiveQueryStart, request));
        app.MapPost(LiveReadPath, (ReadLiveQueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.LiveQueryRead, request));
    }
}
