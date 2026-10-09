using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class PartitionMovementRetireCancellationOutcomeExecution
{
    internal static PartitionMovementRetireCancellationOutcomeResult Execute(DatabaseEngine database,
        PrincipalRecord principal, PartitionMovementRetireCancellationOutcomeQuery query,
        IOptions<DatabaseLimits> limits, TimeProvider clock, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        if (query.MaximumReadBytes <= PartitionMoveProtocol.EmptyCount || query.MaximumReadBytes > database.Limits.MaxQueryReadBytes
            || query.MaximumExaminedRecords <= PartitionMoveProtocol.EmptyCount || query.MaximumExaminedRecords > database.Limits.MaxScanRecords
            || query.MaximumResultBytes <= PartitionMoveProtocol.EmptyCount || query.MaximumResultBytes > database.Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, GrainRoutingProtocol.InvalidRequest); }
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        work.ConstrainLifetime(expiresAt);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        var grant = work.CreateReadGrant(query.MaximumReadBytes, query.MaximumExaminedRecords);
        var snapshot = database.ReadPartitionMovementRetireCancellation(principal.Id, query.Body, work, grant);
        var result = new PartitionMovementRetireCancellationOutcomeResult(snapshot, grant.ReadBytes, grant.ExaminedRecords);
        work.MeasureResult(result);
        work.Check();
        return result;
    }
}
