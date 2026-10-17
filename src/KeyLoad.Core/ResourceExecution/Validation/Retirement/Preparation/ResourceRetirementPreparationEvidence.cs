using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Binds active admission metadata to its actual original canonical outcome before granting drain authority.</summary>
internal static class ResourceRetirementPreparationEvidence
{
    internal static void Source(IKeyValueView view, ResourceRetirementPreparationControlV1 control,
        DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(control.PrincipalId, control.PrepareReceipt.CommandId), limits, observer);
        if (outcome is null || outcome.Fingerprint != control.IntentDigest || outcome.PolicyEpoch != control.PolicyEpoch
            || outcome.Incarnation != control.Catalog.DefaultShard.Incarnation || outcome.Partition is not null
            || outcome.ScopeKind != KeyLoad.Core.Features.ClusterRouting.Contracts.CommandOutcomeScopeKind.Global
            || outcome.Result is null || outcome.Result.Error is not null || outcome.Result.Json is not null
            || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || result.State is not null || result.Fence is not null || result.PreparedOperation is not null || result.OwnerReservation is not null || ResourceRetirementWorkValidation.HasCompletionArm(result)
            || !ResourceRetirementValidation.Same(result.Receipt, control.PrepareReceipt)
            || result.PreparationState is null || !ResourceRetirementValidation.Same(result.PreparationState.Control, control)
            || result.PreparationState.Reservations.IsDefault || !result.PreparationState.Reservations.IsEmpty
            || result.PreparationState.BegunControl is not null || result.PreparationState.OwnerAttempts.IsDefault
            || !result.PreparationState.OwnerAttempts.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }

    internal static void Owner(IKeyValueView view, ResourceRetirementOwnerReservationV1 reservation,
        DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        var context = ResourceRetirementAttemptValidation.Owner(reservation.Control, reservation.Receiver);
        var prepared = ResourceRetirementPreparedOperationStorage.Read(view, context, reservation.Attempt!, limits, observer)
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        var original = prepared.Original ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        var outcome = ResourceRetirementStorage.Read<StoredOutcome>(view,
            KeySpace.GlobalOutcome(original.PrincipalId, original.Id), limits, observer);
        if (prepared.Stage != ResourceRetirementPreparedOperationStage.Persisted
            || prepared.Fingerprint != reservation.OriginalFingerprint
            || outcome is null || outcome.Fingerprint != reservation.OriginalFingerprint
            || outcome.Incarnation != reservation.Receiver.Owner.Incarnation || outcome.Partition is not null
            || outcome.ScopeKind != KeyLoad.Core.Features.ClusterRouting.Contracts.CommandOutcomeScopeKind.Global
            || outcome.Result is null || outcome.Result.Error is not null || outcome.Result.Json is not null
            || outcome.Result.NativeValue is not ResourceRetirementPhaseResultV1 result
            || result.State is not null || result.Fence is not null || result.PreparedOperation is not null || result.PreparationState is not null
            || !ResourceRetirementValidation.Same(result.Receipt, reservation.Receipt)
            || ResourceRetirementWorkValidation.HasCompletionArm(result)
            || !ResourceRetirementValidation.Same(result.OwnerReservation, reservation))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }
}
