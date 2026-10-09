using KeyLoad.Core;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>One unique read grain owns original downward work for authority, producer and actual typed reply.</summary>
internal static class PartitionMovementTransferDataExecution
{
    internal static async Task<PartitionMovementTransferDataResult> ExecuteAsync(INativePartitionMovementTransferRead owner,
        PrincipalRecord principal, PartitionMovementTransferDataCapability query, IOptions<DatabaseLimits> limits,
        TimeProvider clock, DateTimeOffset originalExpiry, CancellationToken cancellationToken)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        var work = new ReadExecutionBudget(limits, clock, cancellationToken);
        work.ConstrainLifetime(originalExpiry);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        var grant = work.CreateReadGrant(query.MaximumReadBytes, query.MaximumExaminedRecords);
        using var scope = work.EnterReadGrant(grant);
        var result = query.Action switch
        {
            PartitionMovementTransferDataAction.Open => new PartitionMovementTransferDataResult(query.Action,
                await owner.OpenAsync(principal, query, work, cancellationToken).ConfigureAwait(false), null, null, PartitionMovementContractNames.EmptyReadCount, PartitionMovementContractNames.EmptyReadCount),
            PartitionMovementTransferDataAction.Page => new PartitionMovementTransferDataResult(query.Action, null,
                await owner.ReadPageAsync(principal, query, work, cancellationToken).ConfigureAwait(false), null, PartitionMovementContractNames.EmptyReadCount, PartitionMovementContractNames.EmptyReadCount),
            PartitionMovementTransferDataAction.Close => new PartitionMovementTransferDataResult(query.Action, null, null,
                await owner.CloseAsync(principal, query, work, cancellationToken).ConfigureAwait(false), PartitionMovementContractNames.EmptyReadCount, PartitionMovementContractNames.EmptyReadCount),
            _ => throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.InvalidRequest)
        };
        result = result with { ReadBytes = grant.ReadBytes, ExaminedRecords = grant.ExaminedRecords };
        work.MeasureResult(result);
        work.CompleteReadGrant(grant);
        return result;
    }
}
