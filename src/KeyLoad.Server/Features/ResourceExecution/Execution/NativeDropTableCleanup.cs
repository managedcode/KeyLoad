using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableCleanup
{
    internal static async Task<ResourceRetirementStateV1> ExecuteAsync(NativeDropTableContext context,
        ResourceRetirementStateV1 state)
    {
        var read = await context.Reads.ExecuteAsync(ResourceRetirementReadOperation.OriginalWorkSeal,
            state.Control.Request, state.Control, context.Source, Guid.Empty, ResourceRetirementReadContract.NoRecords,
            context.Expiry, context.PrincipalId, context.PolicyEpoch, context.Work, context.Metadata, context.Token).ConfigureAwait(false);
        var immutable = read.State ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementWorkValidation.RequireBarrier(immutable, context.Options.Core.DatabaseLimits.Value);
        if (!ResourceRetirementValidation.Same(immutable.Control, state.Control))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        foreach (var receiver in immutable.Control.Directory.Owners)
        {
            context.Work.Check();
            if (state.OwnerCleanupReceipts.Any(value => ResourceRetirementValidation.Same(value.Receiver, receiver)))
            {
                continue;
            }
            var proof = await NativeDropTableProgress.ReadAsync(context, state.Control, receiver, null, release: false).ConfigureAwait(false);
            var cleanup = proof.OwnerCleanup;
            if (cleanup is null)
            {
                foreach (var partition in immutable.FinalInventory!.Entries.Where(entry =>
                    PhysicalOwnerEntryValidation.SameOwner(entry.ObservedOwner, receiver.Owner)).Select(entry => entry.Partition).Distinct())
                {
                    await NativeDropTableCleanupPages.ExecuteAsync(context, immutable, receiver, partition).ConfigureAwait(false);
                }
                var completed = await context.PhaseAsync(immutable.Control, ResourceRetirementPhase.CompleteOwnerCleanup,
                    new ResourceRetirementCompleteOwnerCleanupBodyV1(immutable, receiver), receiver,
                    receiver.Owner.PhysicalShardId).ConfigureAwait(false);
                cleanup = completed.OwnerCleanup ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
            }
            ResourceRetirementFinalizationValidation.Owner(cleanup, immutable, context.Options.Core.DatabaseLimits.Value);
            var recorded = await context.PhaseAsync(state.Control, ResourceRetirementPhase.RecordOwnerCleanup,
                new ResourceRetirementRecordOwnerCleanupBodyV1(state.Control, cleanup), context.Source,
                receiver.Owner.PhysicalShardId).ConfigureAwait(false);
            state = recorded.State ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        return state;
    }
}
