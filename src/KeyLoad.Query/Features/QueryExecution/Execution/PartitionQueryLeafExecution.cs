using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Runs the original native leaf executor under a receiving request's exact downward grant.</summary>
internal static class PartitionQueryLeafExecution
{
    private const string OwnerChangedDetail = "The partition query receiving owner changed.";
    internal static PartitionQueryLeafResultV1 Execute(QueryEngine engine, string principalId,
        PartitionQueryOwnedLeafRequest request, PhysicalShardRecord owner, TimeProvider clock, CancellationToken token)
    {
        var database = engine.PartitionQueryOwner;
        PartitionQueryOwnedLeafValidation.Validate(request, database.Limits, engine.Execution);
        var plan = request.Plan;
        if (request.Owner.PhysicalShardId != owner.PhysicalShardId || request.Owner.Incarnation != owner.Incarnation
            || request.Owner.PlacementEpoch != owner.PlacementEpoch
            || !request.Owner.VoterIds.SequenceEqual(owner.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, OwnerChangedDetail); }
        var budget = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        QueryResultBudgetPolicy.Constrain(budget, engine.Execution);
        var normalized = QueryValidation.Normalize(plan.Request, database.Limits, engine.Execution);
        var grant = budget.CreateReadGrant(plan.MaxReadBytes, plan.MaxExaminedRecords);
        using var admission = database.AdmitQuery(token);
        var result = new PartitionQueryLeafExecutor(engine, database, budget, owner, request.Tenant)
            .Execute(principalId, plan, normalized, grant);
        budget.CheckResult(result);
        return result;
    }
}
