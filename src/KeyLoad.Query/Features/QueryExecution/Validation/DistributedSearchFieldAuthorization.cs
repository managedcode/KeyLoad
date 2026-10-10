using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchFieldAuthorization
{
    internal static void Require(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request)
    {
        if (request.Text is not null)
        { database.Authorization.RequireFieldUse(principal, resource, request.TextField!); }
        if (request.Vector is not null)
        {
            database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
            database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
        }
    }
}
