using KeyLoad.Core;
using KeyLoad.Query.QueryExecution;

namespace KeyLoad.Query;

internal static class QueryFieldAuthorization
{
    internal static void Bind(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, AstQueryRequest request)
    {
        if (request.Query.InnerJoin is not null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, SqlSyntax.UnsupportedJoinDetail);
        }
        Validate(database, principal, resource, request);
        RelationalQueryBinding.Bind(resource, request);
    }

    internal static void Validate(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, AstQueryRequest request)
    {
        RequireSchema(database, principal, resource, request);
        foreach (var field in PredicateEvaluator.Fields(request.Query.Filter)
            .Concat(request.Query.Order.Select(order => order.Path)))
        {
            if (field is not SqlSyntax.MetadataIdPath and not SqlSyntax.MetadataRevisionPath)
            {
                database.Authorization.RequireFieldUse(principal, resource, field);
            }
        }
        PredicateEvaluator.CheckParameters(request.Query.Filter, request.Parameters);
    }

    internal static void RequireSchema(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, AstQueryRequest request)
    {
        var fields = request.Query.Projection.Where(selection => selection.Path != SqlSyntax.Star)
            .Select(selection => selection.Path)
            .Concat(PredicateEvaluator.Fields(request.Query.Filter))
            .Concat(request.Query.Order.Select(order => order.Path));
        if (request.Query.Explain || fields.Any(path => path != SqlSyntax.MetadataIdPath && path != SqlSyntax.MetadataRevisionPath))
        {
            database.Authorization.RequireSchemaVisibility(principal, resource);
        }
    }
}
