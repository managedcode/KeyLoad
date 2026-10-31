using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Query.QueryExecution;

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
        PartitionQueryOwnedLeafValidation.Validate(original);
        database.VerifyStorageOwner(actualOwner, token);
        if (original.Owner != actualOwner)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, OwnerChanged);
        }
        var budget = new ReadExecutionBudget(clock, token);
        if (binding is not null)
        {
            budget.AttachResourceLifetimeBinding(binding);
        }
        budget.ConstrainLifetime(originalExpiry);
        var normalized = QueryValidation.Normalize(original.Plan.Request, engine.Execution);
        using var admission = database.AdmitQuery(token);
        var result = new PartitionQueryLeafExecutor(engine, database, budget, actualOwner, receivingLeaf: true)
            .ExecuteLifetime(principalId, original.Plan, normalized,
                new(request.Definition, request.Coverage, request.Principal));
        return result;
    }
}
