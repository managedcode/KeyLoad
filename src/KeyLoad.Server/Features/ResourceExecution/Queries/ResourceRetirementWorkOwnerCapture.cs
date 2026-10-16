using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementWorkOwnerCapture
{
    internal static async Task<ResourceRetirementWorkCaptureResult> ReadAsync(ResourceRetirementStateV1 sealedState,
        RegisteredPhysicalOwnerV1 owner, ResourceRetirementWorkReads reads, string principalId, long epoch,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var receipt = sealedState.OwnerReceipts.Single(item => item.PhysicalOwner.PhysicalShardId == owner.Owner.PhysicalShardId);
        var fence = new ResourceRetirementOwnerFenceV1(sealedState.Control, receipt);
        var evidence = ImmutableArray.CreateBuilder<ResourceRetirementWorkJoinEvidenceV1>();
        var failures = new List<Exception>();
        var remaining = owner.Owner.VoterIds.Length;
        foreach (var voter in owner.Owner.VoterIds)
        {
            var allocation = remaining--;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                evidence.Add(await reads.ExecuteAsync(fence, owner, voter, null, principalId, epoch,
                    expiry, work, metadata, allocation, token).ConfigureAwait(false));
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return ResourceRetirementWorkCapture.Resolve(new(owner, evidence.ToImmutable(), null));
    }

    internal static void RequireOriginalCohort(ResourceRetirementWorkCaptureResult original,
        ResourceRetirementWorkOwnerReceiptV1 acknowledged)
    {
        if (original.Evidence.Length != acknowledged.Evidence.Length)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        foreach (var evidence in original.Evidence)
        {
            var retained = acknowledged.Evidence.SingleOrDefault(item => item.Witness.VoterId == evidence.Witness.VoterId);
            if (retained is null || !ResourceRetirementValidation.Same(retained.Witness, evidence.Witness))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
    }
}
