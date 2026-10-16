using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.UnitTests.Features.ClusterRouting;
using static KeyLoad.UnitTests.Features.ResourceExecution.ResourceRetirementPreparationValidationData;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceRetirementOriginal8RecoveryAssertions
{
    private const string WrongPrincipal = "wrong-original-reservation-subject";
    private const string ChangedJson = "{}";

    internal static async Task<ReplicatedOperation> RecoveredAsync(ControlledPartitionMovementNode target,
        ResourceRetirementPreparedOperationV1 prepared, ResourceRetirementOriginal8Expectation expected)
    {
        await Assert.That(NativeSerialization.Serialize(prepared).AsSpan().SequenceEqual(expected.PreparedBytes)).IsTrue();
        var original = prepared.Original ?? throw new InvalidOperationException(Invalid);
        await Assert.That(NativeSerialization.Serialize(original).AsSpan().SequenceEqual(expected.OperationBytes)).IsTrue();
        await Assert.That(original.NativePayload.Span.SequenceEqual(expected.NativePayload)).IsTrue();
        await Assert.That(original.Id).IsEqualTo(expected.CommandId);
        await Assert.That(original.Kind).IsEqualTo(OperationKind.ResourceRetirementPhase);
        await Assert.That(original.PrincipalId).IsEqualTo(expected.PrincipalId);
        await Assert.That(original.EvaluatedAt).IsEqualTo(expected.EvaluatedAt);
        await Assert.That(original.PayloadJson).IsEqualTo(expected.PayloadJson);
        await Assert.That(prepared.Fingerprint).IsEqualTo(expected.Fingerprint);
        await Assert.That(NativeOperationFingerprint.Compute(original)).IsEqualTo(expected.Fingerprint);
        var verified = target.Database.VerifyOperationAuthority(original);
        await Assert.That(NativeSerialization.Serialize(verified).AsSpan().SequenceEqual(expected.OperationBytes)).IsTrue();
        return verified;
    }

    internal static async Task RefusalsAsync(ControlledPartitionMovementNode target, ReplicatedOperation original)
    {
        var before = ControlledPartitionMovementExpiryOwnerState.Capture(target);
        foreach (var changed in new[]
        {
            original with { PayloadJson = ChangedJson },
            original with { PrincipalId = WrongPrincipal }
        })
        {
            var error = Assert.ThrowsExactly<KeyLoadException>(() => target.Database.VerifyOperationAuthority(changed));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await before.AssertUnchangedAsync(target);
        }
        await Assert.That(target.Database.VerifyOperationAuthority(original).NativePayload.Span
            .SequenceEqual(original.NativePayload.Span)).IsTrue();
    }

    internal static async Task ReservedAsync(ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt,
        ResourceRetirementOriginal8Expectation expected, long index, ResourceRetirementPhaseResultV1 result)
    {
        var reservation = result.OwnerReservation ?? throw new InvalidOperationException(Invalid);
        var receipt = new ResourceRetirementPhaseReceiptV1(Version, context.Preparation!.Request.RetirementId,
            ResourceRetirementPhase.ReserveOwner, expected.CommandId, context.Preparation.Lifetime,
            Target.Owner, index, context.Preparation.IntentDigest);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(result.Receipt, receipt);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(reservation.Receipt, receipt);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(reservation.Control, context.Preparation);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(reservation.Receiver, Target);
        await ResourceRetirementPreparationValidationAssertions.EqualAsync(reservation.Attempt, attempt);
        await Assert.That(reservation.OriginalFingerprint).IsEqualTo(expected.Fingerprint);
        await Assert.That(reservation.LocalAcceptedMoves.IsEmpty).IsTrue();
        await Assert.That(result.State).IsNull();
        await Assert.That(result.Fence).IsNull();
        await Assert.That(result.PreparedOperation).IsNull();
        await Assert.That(result.PreparationState).IsNull();
    }
}
