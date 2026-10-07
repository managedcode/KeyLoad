using KeyLoad.Core;
using KeyLoad.Query.Features.QueryExecution;

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
    }

    internal static void Validate(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, AstQueryRequest request)
    {
        foreach (var field in PredicateEvaluator.Fields(request.Query.Filter)
            .Concat(request.Query.Order.Select(order => order.Path)))
        {
            database.Authorization.RequireFieldUse(principal, resource, field);
        }
        PredicateEvaluator.CheckParameters(request.Query.Filter, request.Parameters);
    }
}
