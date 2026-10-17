using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Checks immutable Preparing authority without requiring data-inventory provenance.</summary>
internal static class ResourceRetirementPreparationValidation
{
    internal static void Control(ResourceRetirementPreparationControlV1 control, DatabaseLimits limits)
    {
        if (control is null || control.Definition is null || control.Lifetime is null || control.Catalog is null
            || control.Directory is null || control.PolicyEpoch < ResourceRetirementProtocol.Empty
            || !ResourceRetirementValidation.Digest(control.IntentDigest))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Request(control.Request, limits);
        ResourceLifetimeIdentity.Validate(control.Lifetime, control.Request.TenantId, control.Request.DatabaseId, control.Definition);
        if (control.Request.ResourceName != control.Definition.Name || control.Request.ExpectedIncarnation != control.Lifetime.Incarnation)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        JsonData.Identifier(control.PrincipalId);
        PhysicalShardCatalogValidation.ValidateCatalog(control.Catalog);
        PhysicalOwnerDirectoryValidation.Validate(control.Directory);
        if (!PhysicalOwnerEntryValidation.SameOwner(control.Catalog.DefaultShard, control.Directory.ControlOwner))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        Moves(control.SourceAcceptedMoves, limits);
        Receipt(control.PrepareReceipt, control, ResourceRetirementPhase.PrepareRetirement,
            control.Catalog.DefaultShard, ResourceRetirementIdentity.PreparationCommandId(control.Request), limits);
        ResourceRetirementValidation.Bound(control, limits);
    }

    internal static void Reservation(ResourceRetirementOwnerReservationV1 reservation, DatabaseLimits limits)
    {
        if (reservation is null || reservation.Attempt is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        Control(reservation.Control, limits);
        Member(reservation.Control, reservation.Receiver);
        Moves(reservation.LocalAcceptedMoves, limits);
        ResourceRetirementAttemptValidation.Witness(reservation.Attempt,
            ResourceRetirementAttemptValidation.Owner(reservation.Control, reservation.Receiver), limits);
        if (!ResourceRetirementValidation.Digest(reservation.OriginalFingerprint!))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        Receipt(reservation.Receipt, reservation.Control, ResourceRetirementPhase.ReserveOwner,
            reservation.Receiver.Owner, reservation.Attempt.OriginalCommandId, limits);
        ResourceRetirementValidation.Bound(reservation, limits);
    }

    internal static void Status(ResourceRetirementOwnerStatusV1 status, DatabaseLimits limits)
    {
        if (status is null || status.Cut is null || status.Cut.NodeId == Guid.Empty
            || status.Cut.StoreIncarnation == Guid.Empty || status.Cut.ReadGeneration < ResourceRetirementProtocol.Empty
            || status.Cut.StorePosition < ResourceRetirementProtocol.Empty || status.Cut.AppliedIndex < ResourceRetirementProtocol.Empty
            || status.Cut.CatalogRevision <= ResourceRetirementProtocol.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        Reservation(status.Reservation, limits);
        if (status.Cut.StoreIncarnation != status.Reservation.Receiver.Owner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        Moves(status.Remaining, limits);
        if (status.Remaining.Any(move => !Accepted(status.Reservation, move.Partition, move.MoveId)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
        ResourceRetirementValidation.Bound(status, limits);
    }

    internal static void Moves(ImmutableArray<ResourceRetirementAcceptedMoveV1> moves, DatabaseLimits limits)
    {
        if (moves.IsDefault || moves.Length > limits.MaxResults || moves.Length > limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementProtocol.Capacity);
        }
        var seen = new HashSet<(PartitionRef, Guid)>();
        foreach (var move in moves)
        {
            if (move is null || move.Partition is null || move.MoveId == Guid.Empty || !seen.Add((move.Partition, move.MoveId)))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
            }
            DatabaseEngine.ValidatePartition(move.Partition);
        }
        ResourceRetirementValidation.Bound(moves, limits);
    }

    internal static bool Accepted(ResourceRetirementOwnerReservationV1 reservation, PartitionRef partition, Guid moveId)
        => Contains(reservation.Control.SourceAcceptedMoves, partition, moveId) || Contains(reservation.LocalAcceptedMoves, partition, moveId);

    internal static bool Contains(ImmutableArray<ResourceRetirementAcceptedMoveV1> moves, PartitionRef partition, Guid moveId)
        => moves.Any(move => move.Partition == partition && move.MoveId == moveId);

    internal static void Member(ResourceRetirementPreparationControlV1 control, RegisteredPhysicalOwnerV1 receiver)
    {
        if (!PhysicalOwnerEntryValidation.Valid(receiver)
            || !control.Directory.Owners.Any(owner => PhysicalOwnerEntryValidation.Same(owner, receiver)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementProtocol.Changed);
        }
    }

    internal static void Receipt(ResourceRetirementPhaseReceiptV1 receipt, ResourceRetirementPreparationControlV1 control,
        ResourceRetirementPhase phase, PhysicalShardRecord owner, Guid commandId, DatabaseLimits limits)
    {
        if (receipt is null || receipt.Version != ResourceRetirementProtocol.Version || receipt.Phase != phase
            || receipt.RetirementId != control.Request.RetirementId || receipt.CommandId != commandId
            || receipt.IntentDigest != control.IntentDigest || receipt.AppliedPosition <= ResourceRetirementProtocol.Empty
            || !ResourceRetirementValidation.Same(receipt.Lifetime, control.Lifetime)
            || receipt.PhysicalOwner is null || !PhysicalOwnerEntryValidation.SameOwner(receipt.PhysicalOwner, owner))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        ResourceRetirementValidation.Bound(receipt, limits);
    }
}
