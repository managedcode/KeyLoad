using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementPreparationReadiness(ResourceRetirementPreparationReads reads)
{
    internal async Task<ImmutableArray<ResourceRetirementOwnerStatusV1>> ReadAsync(
        ResourceRetirementPreparationStateV1 state, RegisteredPhysicalOwnerV1 source,
        string principalId, long epoch, DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var statuses = ImmutableArray.CreateBuilder<ResourceRetirementOwnerStatusV1>();
        foreach (var owner in state.Control.Directory.Owners.OrderByDescending(value =>
            PhysicalOwnerEntryValidation.SameOwner(value.Owner, source.Owner)))
        {
            foreach (var voter in owner.Owner.VoterIds)
            {
                var status = await reads.StatusAsync(state.Control, owner, voter, principalId, epoch,
                    expiry, work, metadata, token).ConfigureAwait(false);
                if (status is not null)
                {
                    statuses.Add(status);
                }
            }
        }
        var actual = statuses.ToImmutable();
        if (NativeSerialization.Measure(actual) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        ResourceRetirementPreparationStatuses.RequireReady(actual, state);
        work.MeasureResult(actual);
        return actual;
    }
}
