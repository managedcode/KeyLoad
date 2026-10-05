using KeyLoad.Core;

namespace KeyLoad.Query;

internal static class QueryFieldAuthorization
{
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
