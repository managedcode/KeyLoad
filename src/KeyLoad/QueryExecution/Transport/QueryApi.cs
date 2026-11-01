using KeyLoad.Clustering;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class QueryApi
{
    private const string QueryPath = ApiRoutes.Query;
    private const string SearchPath = "/query/search";
    private const string AstPath = ApiRoutes.QueryAst;
    private const string CapabilitiesPath = ApiRoutes.QueryCapabilities;
    private const string LiveStartPath = ApiRoutes.LiveQueryStart;
    private const string LiveReadPath = ApiRoutes.LiveQueryRead;

    internal static void Map(WebApplication app)
    {
        app.MapPost(SearchPath, (SqlGraphSearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.SqlGraphSearch, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(McpToolRoutes.QueryGraphPath, (SqlGraphPathRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.SqlGraphPath, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(SqlOperationProtocol.Route, ExecuteSqlAsync)
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(DistributedSearchProtocol.Route, (DistributedSearchRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.DistributedSearch, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(QueryPath, (QueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.Query, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(AstPath, (AstQueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.AstQuery, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(McpToolRoutes.QueryPartitions, (PartitionQueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.PartitionQuery, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapGet(CapabilitiesPath, (Func<HttpContext, Task<IResult>>)(context =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.QueryCapabilities)))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(LiveStartPath, (StartLiveQueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.LiveQueryStart, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
        app.MapPost(LiveReadPath, (ReadLiveQueryRequest request, HttpContext context) =>
            ApiGrainDispatch.ReadAsync(context, GrainReadKind.LiveQueryRead, request))
            .RequireAuthorization(DatabaseAuthenticationComposition.SubjectPolicy);
    }

    private static async Task<IResult> ExecuteSqlAsync(SqlOperationRequest request, HttpContext context, IOptions<QueryExecutionOptions> queryOptions, IOptions<ServerExecutionOptions> server)
    {
        var operation = SqlOperationCompiler.Compile(request, server.Value.MaximumBodyBytes, queryOptions,
            context.RequestAborted);
        var reply = await CanonicalOperationGateway.ExecuteAsync(context, operation.ReadKind,
            operation.CommandKind, operation.CommandId, operation.Payload, context.RequestAborted);
        return new GrainJsonResult(reply.Payload);
    }
}
