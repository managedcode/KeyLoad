using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementOriginal8RecoveryFlow
{
    internal static async Task ExecuteAsync(IReadOnlyList<ControlledPartitionMovementNode> nodes, CancellationToken token)
    {
        var (context, attempt) = await ResourceRetirementOriginal8RecoverySeed.CreateAsync(nodes, token);
        var source = nodes[SourceIndex];
        var target = nodes[TargetIndex];
        var expected = await PersistAsync(target, context, attempt, token);
        var sourceCut = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        var targetCut = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        // PersistAsync has returned. Only owned expected bytes/scalars survive; its issued operation is not passed onward.
        source.Reopen();
        target.Reopen();
        await sourceCut.AssertUnchangedAsync(source);
        await targetCut.AssertUnchangedAsync(target);
        var sourceWork = ResourceRetirementPreparationValidationFixture.Work(source, token);
        var recoveredAttempt = source.Database.ReadResourceRetirementAttempt(PhysicalShardCatalogFixture.RootPrincipalId,
            context, attempt.AttemptId, sourceWork);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(recoveredAttempt, attempt);
        await Assert.That(sourceWork.ReadBytes).IsGreaterThan(0L);
        var targetWork = ResourceRetirementPreparationValidationFixture.Work(target, token);
        var prepared = target.Database.ReadPreparedResourceRetirementOperation(PhysicalShardCatalogFixture.RootPrincipalId,
            context, recoveredAttempt, targetWork);
        await Assert.That(targetWork.ReadBytes).IsGreaterThan(0L);
        var original = await ResourceRetirementOriginal8RecoveryAssertions.RecoveredAsync(target, prepared, expected);
        await ResourceRetirementOriginal8RecoveryAssertions.RefusalsAsync(target, original);
        var index = target.Journal.Log.State.LastIndex + One;
        var outcome = target.Journal.Submit(original, token);
        await Assert.That(outcome.Error).IsNull();
        var result = outcome.Get<ResourceRetirementPhaseResultV1>();
        var reservation = result.OwnerReservation ?? throw new InvalidOperationException(Invalid);
        await ResourceRetirementOriginal8RecoveryAssertions.ReservedAsync(context, attempt, expected, index, result);
        var after = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(target.Journal.Submit(original, token), outcome);
        await after.AssertUnchangedAsync(target);
        await RecordAsync(source, context, reservation, token);
    }

    private static async Task<ResourceRetirementOriginal8Expectation> PersistAsync(ControlledPartitionMovementNode target,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, CancellationToken token)
    {
        var phase = new ResourceRetirementPhaseCommandV1(ResourceRetirementPreparationValidationData.Version, context.Preparation!.Request.RetirementId,
            ResourceRetirementPhase.ReserveOwner, NativeSerialization.Serialize(new ResourceRetirementReserveOwnerBodyV1(context, attempt)));
        var original = target.Database.CreateResourceRetirementOperation(attempt.OriginalCommandId,
            PhysicalShardCatalogFixture.RootPrincipalId, target.Database.EvaluationClock.GetUtcNow(), phase);
        var persistId = ResourceRetirementIdentity.PreparedOperationCommandId(context, attempt.AttemptId,
            ResourceRetirementPreparedOperationAction.Persist);
        var persisted = await ResourceRetirementPreparationValidationSeed.PhaseAsync(target, context.Preparation.Request,
            ResourceRetirementPhase.PreparedOperation, new ResourceRetirementPreparedOperationBodyV1(context, attempt,
                ResourceRetirementPreparedOperationAction.Persist, original), persistId, token);
        var actual = persisted.PreparedOperation ?? throw new InvalidOperationException(Invalid);
        await Assert.That(actual.Stage).IsEqualTo(ResourceRetirementPreparedOperationStage.Persisted);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual.Context, context);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual.Attempt, attempt);
        await Assert.That(actual.Fingerprint).IsEqualTo(NativeOperationFingerprint.Compute(original));
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual.Receipt, persisted.Receipt);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(actual.PersistReceipt, persisted.Receipt);
        await Assert.That(actual.Original!.NativePayload.Span.SequenceEqual(original.NativePayload.Span)).IsTrue();
        return new(NativeSerialization.Serialize(original), original.NativePayload.ToArray(), original.PayloadJson,
            original.Id, original.PrincipalId, original.EvaluatedAt, NativeOperationFingerprint.Compute(original),
            NativeSerialization.Serialize(actual));
    }

    private static async Task RecordAsync(ControlledPartitionMovementNode source, ResourceRetirementAttemptContextV1 context,
        ResourceRetirementOwnerReservationV1 reservation, CancellationToken token)
    {
        var request = context.Preparation!.Request;
        var id = ResourceRetirementIdentity.AttemptCommandId(request, ResourceRetirementPhase.RecordReservation,
            reservation.Attempt!.AttemptId, reservation.Receiver.Owner.PhysicalShardId);
        var phase = new ResourceRetirementPhaseCommandV1(ResourceRetirementPreparationValidationData.Version, request.RetirementId,
            ResourceRetirementPhase.RecordReservation, NativeSerialization.Serialize(
                new ResourceRetirementRecordReservationBodyV1(context.Preparation, reservation)));
        var original = source.Database.CreateResourceRetirementOperation(id, PhysicalShardCatalogFixture.RootPrincipalId,
            source.Database.EvaluationClock.GetUtcNow(), phase);
        var outcome = source.Journal.Submit(original, token);
        await Assert.That(outcome.Error).IsNull();
        var preparation = source.Database.ReadResourceRetirementPreparation(PhysicalShardCatalogFixture.RootPrincipalId,
            request, ResourceRetirementPreparationValidationFixture.Work(source, token));
        await Assert.That(preparation.Reservations.Length).IsEqualTo(One);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(preparation.Reservations.Single(), reservation);
        var after = ControlledPartitionMovementExpiryOwnerState.Capture(source);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(source.Journal.Submit(original, token), outcome);
        await after.AssertUnchangedAsync(source);
        var repeated = source.Database.ReadResourceRetirementPreparation(PhysicalShardCatalogFixture.RootPrincipalId,
            request, ResourceRetirementPreparationValidationFixture.Work(source, token));
        await Assert.That(repeated.Reservations.Length).IsEqualTo(One);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(repeated.Reservations.Single(), reservation);
    }
}
