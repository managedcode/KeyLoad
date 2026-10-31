using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

internal static class DistributedSearchWitnessRevalidation
{
    private const string ReceivingPrincipalChanged = "The distributed search receiving principal changed.";

    internal static DistributedTextWitness Execute(DatabaseEngine database, string principalId,
        SearchRequest request, string tenant, DistributedTextWitness witness,
        ReadExecutionBudget budget, PartitionQueryLifetimeMetadata? metadata = null)
    {
        using var admission = database.AdmitQuery(budget.Cancellation);
        Func<IKeyValueView, PrincipalRecord, ResourceDefinition, string, DistributedTextWitness> read =
            (view, principal, resource, digest) =>
            {
                budget.Check();
                if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
                {
                    throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingPrincipalChanged);
                }
                DistributedTextWitnessValidation.Require(database, view, principal, resource, digest,
                    witness, request.Partition, budget);
                DistributedSearchFieldAuthorization.Require(database, principal, resource, request, metadata);
                return witness;
            };
        return metadata is null
            ? database.WithPartitionQueryFenceView(principalId, request.Partition, request.Collection, read, budget)
            : database.WithPartitionQueryLifetimeView(principalId, request.Partition, request.Collection, budget, metadata, read);
    }
}
