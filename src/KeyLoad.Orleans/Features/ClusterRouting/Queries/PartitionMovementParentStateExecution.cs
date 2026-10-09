using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Returns actual canonical control rows through one fresh administrator request grain.</summary>
internal static class PartitionMovementParentStateExecution
{
    internal static PartitionMovementParentStateResult Execute(DatabaseEngine database, PrincipalRecord principal,
        PartitionMovementParentStateQuery query, IOptions<DatabaseLimits> limits, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        if (query.MaximumReadBytes <= PartitionMovementContractNames.EmptyReadCount
            || query.MaximumReadBytes > database.Limits.MaxQueryReadBytes || query.MaximumExaminedRecords <= PartitionMovementContractNames.EmptyReadCount
            || query.MaximumExaminedRecords > database.Limits.MaxScanRecords || query.MaximumResultBytes <= PartitionMovementContractNames.EmptyReadCount
            || query.MaximumResultBytes > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.InvalidRequest); }
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        var grant = work.CreateReadGrant(query.MaximumReadBytes, query.MaximumExaminedRecords);
        var authority = database.ReadPartitionMovementParentState(principal.Id, query.Request,
            query.SelectedOriginalPhaseCommandId, work, grant);
        var result = new PartitionMovementParentStateResult(authority, grant.ReadBytes, grant.ExaminedRecords);
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
