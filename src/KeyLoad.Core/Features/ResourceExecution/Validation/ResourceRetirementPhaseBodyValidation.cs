using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks bounded immutable private phase shape before a malformed body can enter a native journal.</summary>
internal static class ResourceRetirementPhaseBodyValidation
{
    internal static Guid Require(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        ResourceRetirementValidation.Bound(phase, limits);
        return phase.Phase switch
        {
            ResourceRetirementPhase.Begin => Begin(phase, limits),
            ResourceRetirementPhase.FenceOwner => Fence(phase, limits),
            ResourceRetirementPhase.RecordOwnerReceipt => Receipt(phase, limits),
            ResourceRetirementPhase.PrepareAttempt => Prepare(phase, limits),
            ResourceRetirementPhase.SealBarrier => Seal(phase, limits),
            ResourceRetirementPhase.PreparedOperation => Prepared(phase, limits),
            ResourceRetirementPhase.PrepareRetirement => Preparing(phase, limits),
            ResourceRetirementPhase.ReserveOwner => Reserve(phase, limits),
            ResourceRetirementPhase.RecordReservation => RecordReservation(phase, limits),
            ResourceRetirementPhase.CommitOwnerWorkJoin or ResourceRetirementPhase.RecordOwnerWorkJoin
                or ResourceRetirementPhase.SealAllWork => ResourceRetirementWorkBodyValidation.Require(phase, limits),
            ResourceRetirementPhase.CleanupPage or ResourceRetirementPhase.RecordOwnerCleanup
                or ResourceRetirementPhase.FinalizeCatalog or ResourceRetirementPhase.ReleaseOwnerTopology
                or ResourceRetirementPhase.RecordOwnerRelease or ResourceRetirementPhase.Finish
                or ResourceRetirementPhase.CompleteOwnerCleanup or ResourceRetirementPhase.PersistDropParent
                or ResourceRetirementPhase.CompleteDropParent => ResourceRetirementCompletionBodyValidation.Require(phase, limits),
            _ => throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid)
        };
    }

    private static Guid Begin(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementBeginBodyV1>(phase);
        ResourceRetirementValidation.Request(body.Request, limits);
        Parent(phase, body.Request.RetirementId);
        ResourceRetirementPreparationValidation.Control(body.Preparation!, limits);
        if (!ResourceRetirementValidation.Same(body.Request, body.Preparation!.Request)
            || body.OwnerStatuses.IsDefaultOrEmpty || body.OwnerStatuses.Length > limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
        }
        var nodes = new HashSet<Guid>();
        foreach (var status in body.OwnerStatuses)
        {
            ResourceRetirementPreparationValidation.Status(status, limits);
            if (!ResourceRetirementValidation.Same(status.Reservation.Control, body.Preparation)
                || !status.Remaining.IsEmpty || !nodes.Add(status.Cut.NodeId))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
            }
        }
        foreach (var owner in body.Preparation.Directory.Owners)
        {
            if (body.OwnerStatuses.Count(status => PhysicalOwnerEntryValidation.SameOwner(
                    status.Reservation.Receiver.Owner, owner.Owner)) != owner.Owner.VoterIds.Length)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementProtocol.Incomplete);
            }
        }
        ResourceRetirementValidation.Bound(body, limits);
        return body.Request.RetirementId;
    }

    private static Guid Fence(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementFenceBodyV1>(phase);
        ResourceRetirementValidation.Control(body.Control, limits);
        Parent(phase, body.Control.Request.RetirementId);
        if (!PhysicalOwnerEntryValidation.Valid(body.Receiver)
            || !PhysicalOwnerEntryValidation.Same(body.Receiver,
                ResourceRetirementStateValidation.Member(body.Control, body.Receiver.Owner)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        return ResourceRetirementIdentity.PhaseCommandId(body.Control.Request,
            phase.Phase, body.Receiver.Owner.PhysicalShardId);
    }

    private static Guid Receipt(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementReceiptBodyV1>(phase);
        ResourceRetirementValidation.Control(body.Control, limits);
        Parent(phase, body.Control.Request.RetirementId);
        var owner = ResourceRetirementStateValidation.Member(body.Control, body.OwnerReceipt?.PhysicalOwner);
        ResourceRetirementValidation.Receipt(body.OwnerReceipt!, body.Control,
            ResourceRetirementPhase.FenceOwner, owner.Owner, limits);
        return ResourceRetirementIdentity.PhaseCommandId(body.Control.Request, phase.Phase, owner.Owner.PhysicalShardId);
    }

    private static Guid Prepare(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementPrepareAttemptBodyV1>(phase);
        ResourceRetirementAttemptContextValidation.Require(body.Context, limits);
        Parent(phase, ResourceRetirementAttemptContextValidation.Request(body.Context).RetirementId);
        if (body.AttemptId == Guid.Empty || body.ExpectedOrdinal < ResourceRetirementProtocol.Empty
            || body.ExpiresAt == DateTimeOffset.MinValue)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        if (body.ExpectedOrdinal >= limits.MaxResults)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
        }
        ResourceRetirementValidation.Bound(body, limits);
        return ResourceRetirementIdentity.AttemptCommandId(body.Context, phase.Phase, body.AttemptId);
    }

    private static Guid Seal(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementSealBodyV1>(phase);
        ResourceRetirementValidation.Control(body.Control, limits);
        Parent(phase, body.Control.Request.RetirementId);
        ResourceRetirementAttemptValidation.Witness(body.Attempt!, body.Control, limits);
        ResourceRetirementInventoryValidation.Require(body.Inventory, body.Control, limits);
        if (!ResourceRetirementValidation.Same(body.Attempt, body.Inventory.Control.RetirementAttempt))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        return body.Attempt!.OriginalCommandId;
    }

    private static Guid Prepared(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementPreparedOperationBodyV1>(phase);
        ResourceRetirementAttemptContextValidation.Require(body.Context, limits);
        ResourceRetirementAttemptValidation.Witness(body.Attempt, body.Context, limits);
        Parent(phase, ResourceRetirementAttemptContextValidation.Request(body.Context).RetirementId);
        if (!Enum.IsDefined(body.Action))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        if (body.Action == ResourceRetirementPreparedOperationAction.Persist)
        {
            if (body.Original is null || body.Original.Kind != OperationKind.ResourceRetirementPhase
                || body.Original.Id != body.Attempt.OriginalCommandId || body.Original.NativePayload.IsEmpty
                || body.Context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier
                    && body.Original.PrincipalId != body.Attempt.PrincipalId)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
            }
        }
        else if (body.Original is not null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementValidation.Bound(body, limits);
        return ResourceRetirementIdentity.PreparedOperationCommandId(body.Context, body.Attempt.AttemptId, body.Action);
    }

    private static Guid Preparing(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementPrepareBodyV1>(phase);
        ResourceRetirementValidation.Request(body.Request, limits);
        Parent(phase, body.Request.RetirementId);
        return ResourceRetirementIdentity.PreparationCommandId(body.Request);
    }

    private static Guid Reserve(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementReserveOwnerBodyV1>(phase);
        ResourceRetirementAttemptContextValidation.Require(body.Context, limits);
        if (body.Context.Purpose != ResourceRetirementAttemptPurpose.OwnerReservation)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        ResourceRetirementAttemptValidation.Witness(body.Attempt, body.Context, limits);
        Parent(phase, ResourceRetirementAttemptContextValidation.Request(body.Context).RetirementId);
        ResourceRetirementValidation.Bound(body, limits);
        return body.Attempt.OriginalCommandId;
    }

    private static Guid RecordReservation(ResourceRetirementPhaseCommandV1 phase, DatabaseLimits limits)
    {
        var body = Read<ResourceRetirementRecordReservationBodyV1>(phase);
        ResourceRetirementPreparationValidation.Control(body.Control, limits);
        ResourceRetirementPreparationValidation.Reservation(body.Reservation, limits);
        Parent(phase, body.Control.Request.RetirementId);
        if (!ResourceRetirementValidation.Same(body.Control, body.Reservation.Control))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementProtocol.Changed);
        }
        return ResourceRetirementIdentity.AttemptCommandId(body.Control.Request, phase.Phase,
            body.Reservation.Attempt!.AttemptId, body.Reservation.Receiver.Owner.PhysicalShardId);
    }

    private static T Read<T>(ResourceRetirementPhaseCommandV1 phase) where T : class
        => NativeSerialization.Deserialize<T>(phase.Body.Span)
            ?? throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);

    private static void Parent(ResourceRetirementPhaseCommandV1 phase, Guid retirementId)
    {
        if (phase.RetirementId != retirementId)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
    }
}
