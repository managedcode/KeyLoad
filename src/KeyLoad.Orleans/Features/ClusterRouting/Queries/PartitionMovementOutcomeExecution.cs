using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Reads one stored original outcome under fresh authority without issuing an effect.</summary>
internal static class PartitionMovementOutcomeExecution
{
    private const int MinimumCount = 1;

    internal static PartitionMovementOutcomeWitness Execute(DatabaseEngine database,
        PrincipalRecord principal, PartitionMovementOutcomeQuery query, IOptions<DatabaseLimits> configuredLimits, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GrainRequestAuthority.RequireAdministrator(principal);
        var limits = database.Limits;
        if (query.PhaseCommandId == Guid.Empty
            || query.MaximumReadBytes < MinimumCount
            || query.MaximumReadBytes > Math.Min(limits.MaxBatchBytes, limits.MaxQueryReadBytes)
            || query.MaximumExaminedRecords < MinimumCount
            || query.MaximumExaminedRecords > Math.Min(limits.MaxBatchMutations, limits.MaxScanRecords)
            || query.MaximumResultBytes < MinimumCount || query.MaximumResultBytes > limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var work = new ReadExecutionBudget(configuredLimits, clock, cancellationToken);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        var grant = work.CreateReadGrant(query.MaximumReadBytes, query.MaximumExaminedRecords);
        var result = database.ResolveVerifiedPartitionMovementOutcome(principal.Id,
            query.Original, query.PhaseCommandId, work, grant);
        var witness = new PartitionMovementOutcomeWitness(result, grant.ReadBytes, grant.ExaminedRecords);
        work.MeasureResult(witness);
        work.Check();
        return witness;
    }
}
