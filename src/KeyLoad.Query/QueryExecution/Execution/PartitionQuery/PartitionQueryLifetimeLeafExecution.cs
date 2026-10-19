using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Executes the canonical leaf under mandatory source metadata and lifetime coverage.</summary>
internal static class PartitionQueryLifetimeLeafExecution
{
    private const string Invalid = "The partition query lifetime request is invalid.";
    private const string OwnerChanged = "The partition query receiving owner changed.";

    internal static PartitionQueryLeafResult ExecuteBound(QueryEngine engine, string principalId,
        PartitionQueryOwnedLeafRequest request, PhysicalShardRecord actualOwner, TimeProvider clock,
        DateTimeOffset originalExpiry, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return Execute(engine, principalId, request, actualOwner, clock, originalExpiry, token, binding);
    }

    internal static PartitionQueryLeafResult Execute(QueryEngine engine, string principalId,
        PartitionQueryOwnedLeafRequest request, PhysicalShardRecord actualOwner, TimeProvider clock,
        DateTimeOffset originalExpiry, CancellationToken token, IResourceLifetimeWorkBinding? binding = null)
    {
        var database = engine.PartitionQueryOwner;
        if (request is null || request.Definition is null || request.Coverage is null
            || request.Principal is null || request.Principal.Id != principalId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Invalid);
        }
        var original = request;
        PartitionQueryOwnedLeafValidation.Validate(original, database.Limits, engine.Execution);
        database.VerifyStorageOwner(actualOwner, token);
        if (original.Owner != actualOwner)
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
        var result = new PartitionQueryLeafExecutor(engine, database, budget, actualOwner, receivingLeaf: true)
            .ExecuteLifetime(principalId, original.Plan, normalized, grant,
                new(request.Definition, request.Coverage, request.Principal));
        budget.CheckResult(result);
        return result;
    }
}
