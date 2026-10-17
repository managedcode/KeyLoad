using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeDropTableCompletion
{
    internal static async Task<ResourceRetirementStateV1> ExecuteAsync(NativeDropTableContext context,
        ResourceRetirementStateV1 state)
    {
        if (state.Stage == ResourceRetirementStage.Completed)
        {
            ResourceRetirementCompletionValidation.RequireCompleted(state, context.Options.Core.DatabaseLimits.Value);
            return state;
        }
        if (state.Finalization is null)
        {
            state = await NativeDropTableCleanup.ExecuteAsync(context, state).ConfigureAwait(false);
            var finalized = await context.PhaseAsync(state.Control, ResourceRetirementPhase.FinalizeCatalog,
                new ResourceRetirementFinalizeCatalogBodyV1(state.Control), context.Source,
                context.Source.Owner.PhysicalShardId).ConfigureAwait(false);
            state = finalized.State ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        state = await ReleaseAsync(context, state).ConfigureAwait(false);
        var finished = await context.PhaseAsync(state.Control, ResourceRetirementPhase.Finish,
            new ResourceRetirementFinishBodyV1(state.Control), context.Source, context.Source.Owner.PhysicalShardId).ConfigureAwait(false);
        state = finished.State ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        ResourceRetirementCompletionValidation.RequireCompleted(state, context.Options.Core.DatabaseLimits.Value);
        return state;
    }

    private static async Task<ResourceRetirementStateV1> ReleaseAsync(NativeDropTableContext context,
        ResourceRetirementStateV1 state)
    {
        var finalization = state.Finalization
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        foreach (var receiver in state.Control.Directory.Owners)
        {
            context.Work.Check();
            if (state.OwnerReleaseReceipts.Any(value => ResourceRetirementValidation.Same(value.Receiver, receiver)))
            {
                continue;
            }
            var proof = await NativeDropTableProgress.ReadAsync(context, state.Control, receiver, null, release: true).ConfigureAwait(false);
            var release = proof.OwnerRelease;
            if (release is null)
            {
                var result = await context.PhaseAsync(state.Control, ResourceRetirementPhase.ReleaseOwnerTopology,
                    new ResourceRetirementReleaseOwnerTopologyBodyV1(finalization, receiver), receiver,
                    receiver.Owner.PhysicalShardId).ConfigureAwait(false);
                release = result.OwnerRelease ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
            }
            var recorded = await context.PhaseAsync(state.Control, ResourceRetirementPhase.RecordOwnerRelease,
                new ResourceRetirementRecordOwnerReleaseBodyV1(state.Control, release), context.Source,
                receiver.Owner.PhysicalShardId).ConfigureAwait(false);
            state = recorded.State ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);
        }
        return state;
    }
}
