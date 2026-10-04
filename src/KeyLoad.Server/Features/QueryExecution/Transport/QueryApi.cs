using KeyLoad.Orleans;
using KeyLoad.Query;

namespace KeyLoad.Server;

internal static class QueryApi
{
    private const string QueryPath = "/v1/query";
    private const string SearchPath = "/v1/query/search";
    private const string AstPath = "/v1/query/ast";
    private const string CapabilitiesPath = "/v1/query/capabilities";
    private const string LiveStartPath = "/v1/query/live/start";
    private const string LiveReadPath = "/v1/query/live/read";

    internal static void Map(WebApplication app)
    {
        app.MapPost(SearchPath, (SqlGraphSearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.SqlGraphSearch, request));
        app.MapPost(SqlOperationProtocol.Route, ExecuteSqlAsync);
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

    private static async Task<IResult> ExecuteSqlAsync(SqlOperationRequest request, HttpContext context,
        KeyLoad.Core.DatabaseEngine database, KeyLoad.Core.HttpAdmissionGovernor admission)
    {
        var operation = SqlOperationCompiler.Compile(request, database.Limits, admission.Limits.MaxBodyBytes,
            context.RequestAborted);
        var reply = await CanonicalOperationGateway.ExecuteAsync(context, operation.ReadKind,
            operation.CommandKind, operation.CommandId, operation.Payload, context.RequestAborted).ConfigureAwait(false);
        return new GrainJsonResult(reply.Payload);
    }
}
