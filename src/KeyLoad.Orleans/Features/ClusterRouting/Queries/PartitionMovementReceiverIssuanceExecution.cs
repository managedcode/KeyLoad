using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Observation-only actual first issuance joins row and native outcome under one downward bounded read.</summary>
internal static class PartitionMovementReceiverIssuanceExecution
{
    internal static PartitionMovementReceiverIssuanceResult Execute(DatabaseEngine database, PrincipalRecord principal,
        PartitionMovementReceiverIssuanceQuery query, IOptions<DatabaseLimits> limits, TimeProvider clock,
        DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        if (query.MaximumReadBytes <= PartitionMovementContractNames.EmptyReadCount || query.MaximumReadBytes > database.Limits.MaxQueryReadBytes
            || query.MaximumExaminedRecords <= PartitionMovementContractNames.EmptyReadCount || query.MaximumExaminedRecords > database.Limits.MaxScanRecords
            || query.MaximumResultBytes <= PartitionMovementContractNames.EmptyReadCount || query.MaximumResultBytes > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.InvalidRequest); }
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        work.ConstrainLifetime(expiresAt);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        var grant = work.CreateReadGrant(query.MaximumReadBytes, query.MaximumExaminedRecords);
        var snapshot = database.ReadPartitionMovementReceiverIssuance(principal.Id, query.OriginalPhaseCommandId,
            query.OriginalEnvelope, query.OriginalAuthorization, work, grant);
        var result = new PartitionMovementReceiverIssuanceResult(snapshot, grant.ReadBytes, grant.ExaminedRecords);
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
