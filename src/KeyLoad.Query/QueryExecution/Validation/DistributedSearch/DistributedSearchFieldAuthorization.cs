using KeyLoad.Core;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchFieldAuthorization
{
    internal static void Require(DatabaseEngine database, PrincipalRecord principal,
        ResourceDefinition resource, SearchRequest request, PartitionQueryLifetimeMetadata? metadata = null)
    {
        if (metadata is not null)
        {
            database.Authorization.RequireSchemaVisibility(principal, resource);
            database.Authorization.RequireSchemaVisibility(metadata.Principal, metadata.Definition);
            Require(database, metadata.Principal, metadata.Definition, request);
        }
        if (request.Text is not null)
        {
            database.Authorization.RequireFieldUse(principal, resource, request.TextField!);
        }
        if (request.Vector is not null)
        {
            database.Authorization.Require(principal, request.Partition, request.Collection, Capability.VectorSearch);
            database.Authorization.RequireFieldUse(principal, resource, request.VectorField!);
        }
    }
}
