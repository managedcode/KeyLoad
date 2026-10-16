using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageNeverIssuedRecovery
{
    internal static async Task ExecuteAsync(NativeDropPageAttemptState state)
    {
        var attempt = await NativeDropPageRecoveryPrepare.PrepareAsync(state).ConfigureAwait(false);
        var original = await NativeDropPageRecoveryPrepare.IssueAsync(state, attempt, tiny: false).ConfigureAwait(false);
        var persistId = ResourceRetirementIdentity.PreparedOperationCommandId(state.Scope, attempt.AttemptId,
            ResourceRetirementPreparedOperationAction.Persist);
        var held = state.Profile.Observer.Hold(persistId, KeyLoad.Clustering.GrainRequestPhase.BeforeSubmit);
        var pending = NativeDropPageRecoveryPrepare.PersistAsync(state, attempt, original);
        var issuedPersist = await NativeDropHeldPersistObservation.JoinExpiredAsync(state, pending, held, attempt).ConfigureAwait(false);
        await Assert.That(issuedPersist.Id).IsEqualTo(persistId);
        // The injected META may now be terminal. Only the original public parent's own META may continue.
        var closeId = ResourceRetirementIdentity.PreparedOperationCommandId(state.Scope, attempt.AttemptId,
            ResourceRetirementPreparedOperationAction.Close);
        var closed = state.Profile.Observer.Hold(closeId);
        var sealId = ResourceRetirementIdentity.PhaseCommandId(state.OriginalState.Control.Request,
            ResourceRetirementPhase.SealAllWork, state.Context.Source.Owner.PhysicalShardId);
        state.Profile.Observer.Release(sealId);
        await NativeDropHeldPersistObservation.WaitPublicAsync(state, closed).ConfigureAwait(false);
        await NativeDropPageRecoveryAssertions.RetainedAsync(state, attempt, original,
            ResourceRetirementPreparedOperationStage.Closed).ConfigureAwait(false);
        var beforePersist = await NativeDropPageRecoveryImages.CaptureAsync(state.Profile, state.Context.Token).ConfigureAwait(false);
        var latePersist = await state.Profile.Source.Partition.Coordinator.SubmitVerifiedAsync(
            issuedPersist, state.Context.Token).ConfigureAwait(false);
        await Assert.That(latePersist.Error).IsEqualTo(ErrorCode.CursorExpired);
        await NativeDropPageFailureImages.RequireAsync(state, issuedPersist, beforePersist, ErrorCode.CursorExpired).ConfigureAwait(false);
        var image = await NativeDropPageRecoveryImages.CaptureAsync(state.Profile, state.Context.Token).ConfigureAwait(false);
        _ = await NativeDropPageRecoveryAssertions.RefusedAsync(
            () => state.Profile.Source.Partition.Coordinator.SubmitVerifiedAsync(original, state.Context.Token),
            ErrorCode.TokenInvalidated).ConfigureAwait(false);
        await NativeDropPageRecoveryImages.SameAsync(state.Profile, image, state.Context.Token).ConfigureAwait(false);
        await NativeDropPageRecoveryAssertions.RetainedAsync(state, attempt, original,
            ResourceRetirementPreparedOperationStage.Closed).ConfigureAwait(false);
        state.ReplacedAttempt = attempt.AttemptId;
        state.Profile.Observer.Release(closeId);
    }
}
