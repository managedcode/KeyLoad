using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>The selected V2 capability never accepts an old native root or manufactures source metadata.</summary>
internal static class PartitionQueryLifetimeLeafExecution
{
    private const string Invalid = "The partition query lifetime request is invalid.";
    private const string OwnerChanged = "The partition query receiving owner changed.";

    internal static PartitionQueryLeafResultV1 ExecuteBound(QueryEngine engine, string principalId,
        PartitionQueryOwnedLeafRequestV2 request, PhysicalShardRecord actualOwner, TimeProvider clock,
        DateTimeOffset originalExpiry, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Execute(engine, principalId, request, actualOwner, clock, originalExpiry, token, binding);
    }

    internal static PartitionQueryLeafResultV1 Execute(QueryEngine engine, string principalId,
        PartitionQueryOwnedLeafRequestV2 request, PhysicalShardRecord actualOwner, TimeProvider clock,
        DateTimeOffset originalExpiry, CancellationToken token, IResourceLifetimeWorkBinding? binding = null)
    {
        var database = engine.PartitionQueryOwner;
        if (request is null || request.Original is null || request.Definition is null || request.Coverage is null
            || request.Principal is null || request.Principal.Id != principalId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
        var original = request.Original;
        PartitionQueryOwnedLeafValidation.Validate(original, database.Limits, engine.Execution);
        PartitionControlLifetimeCoverage.Require(request.Coverage, original.Plan.Partition,
            [request.Definition], database.Limits.MaxBatchBytes);
        if (!PhysicalOwnerEntryValidation.SameOwner(original.Owner, actualOwner)
            || database.Store.Identity.Incarnation != actualOwner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OwnerChanged);
        }
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        QueryResultBudgetPolicy.Constrain(budget, engine.Execution);
        budget.ConstrainLifetime(originalExpiry);
        var normalized = QueryValidation.Normalize(original.Plan.Request, database.Limits, engine.Execution);
        var grant = budget.CreateReadGrant(original.Plan.MaxReadBytes, original.Plan.MaxExaminedRecords);
        using var admission = database.AdmitQuery(token);
        var result = new PartitionQueryLeafExecutor(engine, database, budget, actualOwner, original.Tenant)
            .ExecuteLifetime(principalId, original.Plan, normalized, grant,
                new(request.Definition, request.Coverage, request.Principal));
        budget.CheckResult(result);
        return result;
    }
}
