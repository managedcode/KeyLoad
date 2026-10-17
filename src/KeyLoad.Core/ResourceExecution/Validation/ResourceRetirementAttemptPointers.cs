using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Maps each source pointer to exactly one frozen receiver without trusting array order.</summary>
internal static class ResourceRetirementAttemptPointers
{
    internal static bool Belongs(ResourceRetirementAttemptV1 attempt, ResourceRetirementPreparationControlV1 control,
        RegisteredPhysicalOwnerV1 receiver)
        => attempt.OriginalCommandId == ResourceRetirementIdentity.AttemptCommandId(control.Request,
            ResourceRetirementPhase.ReserveOwner, attempt.AttemptId, receiver.Owner.PhysicalShardId);

    internal static void Require(ResourceRetirementPreparationStateV1 state, DatabaseLimits limits)
    {
        if (state.OwnerAttempts.IsDefault || state.OwnerAttempts.Length > state.Control.Directory.Owners.Length
            || state.OwnerAttempts.Length > limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        var seen = new HashSet<Guid>();
        foreach (var attempt in state.OwnerAttempts)
        {
            if (attempt is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            var receiver = state.Control.Directory.Owners.SingleOrDefault(owner => Belongs(attempt, state.Control, owner))
                ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            ResourceRetirementAttemptValidation.Require(attempt,
                ResourceRetirementAttemptValidation.Owner(state.Control, receiver), limits);
            if (!seen.Add(receiver.Owner.PhysicalShardId))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
        foreach (var reservation in state.Reservations)
        {
            var attempt = state.OwnerAttempts.SingleOrDefault(value => Belongs(value, state.Control, reservation.Receiver));
            if (attempt is null || attempt.Stage != ResourceRetirementAttemptStage.Completed
                || !ResourceRetirementValidation.Same(ResourceRetirementAttemptValidation.Prepared(attempt), reservation.Attempt)
                || !ResourceRetirementValidation.Same(attempt.TerminalReceipt, reservation.Receipt)
                || attempt.OriginalFingerprint != reservation.OriginalFingerprint)
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
    }

    internal static void ValidateStored(IKeyValueView view, ResourceRetirementPreparationStateV1 state,
        DatabaseLimits limits, StorageReadObserver? observer = null)
    {
        foreach (var attempt in state.OwnerAttempts)
        {
            var receiver = state.Control.Directory.Owners.Single(owner => Belongs(attempt, state.Control, owner));
            var retained = ResourceRetirementAttemptStorage.Read(view,
                ResourceRetirementAttemptValidation.Owner(state.Control, receiver), attempt.AttemptId, limits, observer);
            if (!ResourceRetirementValidation.Same(retained, attempt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
        }
    }

}
