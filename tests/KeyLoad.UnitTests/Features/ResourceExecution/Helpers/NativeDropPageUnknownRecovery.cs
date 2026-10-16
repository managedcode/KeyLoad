using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageUnknownRecovery
{
    internal static async Task ExecuteAsync(NativeDropPageAttemptState state)
    {
        var attempt = await NativeDropPageRecoveryPrepare.PrepareAsync(state).ConfigureAwait(false);
        var original = await NativeDropPageRecoveryPrepare.IssueAsync(state, attempt, tiny: false).ConfigureAwait(false);
        _ = await NativeDropPageRecoveryPrepare.PersistAsync(state, attempt, original).ConfigureAwait(false);
        await NativeDropPageRecoveryPrepare.ExpireAsync(state, attempt).ConfigureAwait(false);
        var image = await NativeDropPageRecoveryImages.CaptureAsync(state.Profile, state.Context.Token).ConfigureAwait(false);
        _ = await NativeDropPageRecoveryAssertions.RefusedAsync(
            () => NativeDropPageRecoveryPrepare.CloseAsync(state, attempt), ErrorCode.RecoveryRequired).ConfigureAwait(false);
        await NativeDropPageRecoveryImages.SameAsync(state.Profile, image, state.Context.Token).ConfigureAwait(false);
        await NativeDropPageRecoveryAssertions.RetainedAsync(state, attempt, original,
            ResourceRetirementPreparedOperationStage.Persisted).ConfigureAwait(false);
        var nextId = Guid.NewGuid();
        var next = new ResourceRetirementPrepareAttemptBodyV1(state.Scope, nextId, attempt.Ordinal, state.Context.Expiry);
        _ = await NativeDropPageRecoveryAssertions.RefusedAsync(() => state.Context.Phases.ExecuteAsync(
            state.OriginalState.Control.Request, ResourceRetirementPhase.PrepareAttempt, next, state.Context.Source,
            state.Receiver.Owner.PhysicalShardId, state.Context.PrincipalId, state.Context.PolicyEpoch,
            state.Context.Expiry, state.Context.Work, state.Context.Metadata, state.Context.Token, nextId), ErrorCode.Conflict).ConfigureAwait(false);
        await NativeDropPageRecoveryAssertions.RetainedAsync(state, attempt, original,
            ResourceRetirementPreparedOperationStage.Persisted).ConfigureAwait(false);
        state.Profile.Observer.ReleaseAll();
        var reply = await state.Call.Original.ConfigureAwait(false);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.RecoveryRequired);
        await state.Profile.Observer.JoinSelectedAsync(state.Context.Token).ConfigureAwait(false);
        for (var cold = default(int); cold < NativeDropRuntimeProtocol.ColdReopens; cold++)
        {
            await state.Profile.RestartAsync(state.Context.Token).ConfigureAwait(false);
            await NativeDropPageRecoveryAssertions.RetainedAsync(state, attempt, original,
                ResourceRetirementPreparedOperationStage.Persisted).ConfigureAwait(false);
            var beforeRetry = await NativeDropPageRecoveryImages.CaptureAsync(state.Profile, state.Context.Token).ConfigureAwait(false);
            var retry = NativeDropPublicSubmission.Start(state.Profile, state.Lookup.Request, state.Lookup.CommandId, state.Context.Token);
            await Assert.That((await retry.Original.ConfigureAwait(false)).Error).IsEqualTo(ErrorCode.RecoveryRequired);
            await NativeDropPageRecoveryImages.SameAsync(state.Profile, beforeRetry, state.Context.Token).ConfigureAwait(false);
            await NativeDropPageRecoverySetup.HealthyOtherAsync(state, state.Context.Token).ConfigureAwait(false);
        }
        state.Profile.RetainRoots(); // Preserve the genuine unresolved original and its native evidence.
    }
}
