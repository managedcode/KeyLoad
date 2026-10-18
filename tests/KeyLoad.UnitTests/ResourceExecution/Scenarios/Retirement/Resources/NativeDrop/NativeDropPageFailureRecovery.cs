namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class NativeDropPageFailureRecovery
{
    internal static async Task ExecuteAsync(NativeDropPageAttemptState state)
    {
        var attempt = await NativeDropPageRecoveryPrepare.PrepareAsync(state).ConfigureAwait(false);
        var original = await NativeDropPageRecoveryPrepare.IssueAsync(state, attempt, tiny: true).ConfigureAwait(false);
        var prepared = await NativeDropPageRecoveryPrepare.PersistAsync(state, attempt, original).ConfigureAwait(false);
        var before = await NativeDropPageRecoveryImages.CaptureAsync(state.Profile, state.Context.Token).ConfigureAwait(false);
        _ = await NativeDropPageRecoveryAssertions.RefusedAsync(() => state.Context.Phases.SubmitOriginalAsync(prepared,
            state.Receiver, state.Context.PrincipalId, state.Context.PolicyEpoch, attempt.ExpiresAt,
            state.Context.Work, state.Context.Metadata, state.Context.Token), ErrorCode.BudgetExceeded).ConfigureAwait(false);
        await NativeDropPageFailureImages.RequireAsync(state, original, before, ErrorCode.BudgetExceeded).ConfigureAwait(false);
        var closed = await NativeDropPageRecoveryPrepare.CloseAsync(state, attempt).ConfigureAwait(false);
        await Assert.That(closed.Stage).IsEqualTo(ResourceRetirementPreparedOperationStage.Closed);
        await Assert.That(closed.Error).IsEqualTo(ErrorCode.BudgetExceeded);
        await Assert.That(closed.Original!.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
        var healthy = await NativeDropTablePageSequence.ExecuteAsync(state.Context, state.OriginalState, state.Receiver,
            state.Scope.Page!.Partition, previous: null).ConfigureAwait(false);
        await Assert.That(healthy.AttemptId).IsNotEqualTo(attempt.AttemptId);
        await Assert.That(healthy.Ordinal).IsEqualTo(state.Scope.Page.Ordinal);
        ResourceRetirementCleanupCheckpointValidation.Require(healthy, state.Profile.Source.Database.Limits);
    }
}
