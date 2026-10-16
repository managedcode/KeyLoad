using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementQuiescenceData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementQuiescenceAttempt
{
    internal static async Task<ResourceRetirementAttemptV1> PrepareAsync(ResourceRetirementQuiescenceState state,
        ResourceRetirementAttemptContextV1 context, long ordinal, ResourceRetirementPreparedOperationV1? previous,
        CancellationToken token)
    {
        var id = Guid.NewGuid();
        var expires = state.Source.Database.EvaluationClock.GetUtcNow().AddSeconds(state.Source.Database.Limits.QueryDeadlineSeconds);
        var body = new ResourceRetirementPrepareAttemptBodyV1(context, id, ordinal, expires) { PreviousClosed = previous };
        var commandId = ResourceRetirementIdentity.AttemptCommandId(context, ResourceRetirementPhase.PrepareAttempt, id);
        var result = await ResourceRetirementQuiescencePhases.SubmitAsync(state, state.Source,
            ResourceRetirementPhase.PrepareAttempt, commandId, body, token);
        var actual = state.Source.Database.ReadResourceRetirementAttempt(Root, context, id,
            ResourceRetirementQuiescencePhases.Work(state.Source, token));
        var expected = new ResourceRetirementAttemptV1(id, ordinal + One, expires, result.Receipt,
            ResourceRetirementAttemptStage.Prepared, Root, ResourceRetirementQuiescencePhases.Epoch(state.Source),
            ResourceRetirementIdentity.AttemptCommandId(state.Request, ResourceRetirementPhase.ReserveOwner,
                id, context.Receiver!.Owner.PhysicalShardId), null, null, null, null);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(expected, actual);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(actual,
            result.PreparationState!.OwnerAttempts.Single(attempt => attempt.AttemptId == id));
        return actual;
    }

    internal static ReplicatedOperation Issue(ControlledPartitionMovementNode owner,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, string principal)
        => owner.Database.CreateResourceRetirementOperation(attempt.OriginalCommandId, principal,
            owner.Database.EvaluationClock.GetUtcNow(), new(Version, context.Preparation!.Request.RetirementId,
                ResourceRetirementPhase.ReserveOwner, NativeSerialization.Serialize(new ResourceRetirementReserveOwnerBodyV1(context, attempt))));

    internal static async Task UnknownAsync(ResourceRetirementQuiescenceState state, ControlledPartitionMovementNode owner,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, CancellationToken token)
    {
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => owner.Database.ReadPreparedResourceRetirementOperation(
            Root, context, attempt, ResourceRetirementQuiescencePhases.Work(owner, token)));
        state.ExpectedFailures.Add(failure);
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.RecoveryRequired);
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
    }

    internal static async Task BlockAdvanceAsync(ResourceRetirementQuiescenceState state,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, CancellationToken token)
    {
        var next = Guid.NewGuid();
        var body = new ResourceRetirementPrepareAttemptBodyV1(context, next, attempt.Ordinal, attempt.ExpiresAt);
        var operation = state.Source.Database.CreateResourceRetirementOperation(
            ResourceRetirementIdentity.AttemptCommandId(context, ResourceRetirementPhase.PrepareAttempt, next), Root,
            state.Source.Database.EvaluationClock.GetUtcNow(), new(Version, state.Request.RetirementId,
                ResourceRetirementPhase.PrepareAttempt, NativeSerialization.Serialize(body)));
        var result = await ResourceRetirementQuiescenceReservationFailures.SubmitAsync(state, state.Source, operation,
            ErrorCode.Conflict, ResourceRetirementProtocol.Changed, token);
        await ResourceRetirementQuiescenceAssertions.EqualAsync(attempt,
            state.Source.Database.ReadResourceRetirementAttempt(Root, context, attempt.AttemptId,
                ResourceRetirementQuiescencePhases.Work(state.Source, token)));
        await Assert.That(result.NativeValue).IsNull();
    }
}
