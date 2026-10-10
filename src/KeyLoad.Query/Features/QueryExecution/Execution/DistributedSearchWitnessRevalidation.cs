using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Query.Features.QueryExecution;

internal static class DistributedSearchWitnessRevalidation
{
    private const string ReceivingPrincipalChanged = "The distributed search receiving principal changed.";

    internal static DistributedTextWitnessV1 Execute(DatabaseEngine database, string principalId,
        SearchRequest request, string tenant, DistributedTextWitnessV1 witness,
        ReadExecutionBudget budget, ReadExecutionBudgetReadGrant grant)
    {
        using var admission = database.AdmitQuery(budget.Cancellation);
        using var charged = budget.EnterReadGrant(grant);
        return database.WithPartitionQueryFenceView(principalId, request.Partition, request.Collection, grant,
            (view, principal, resource, digest) =>
            {
                budget.Check();
                if (!string.Equals(principal.TenantId, tenant, StringComparison.Ordinal))
                { throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingPrincipalChanged); }
                DistributedTextWitnessValidation.Require(database, view, principal, resource, digest,
                    witness, request.Partition, grant, budget);
                DistributedSearchFieldAuthorization.Require(database, principal, resource, request);
                return witness;
            });
    }
}
