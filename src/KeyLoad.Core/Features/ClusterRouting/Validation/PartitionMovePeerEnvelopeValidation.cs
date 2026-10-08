using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMovePeerEnvelopeValidation
{
    internal static void RequireStructure(PartitionMovePeerEnvelope envelope, int maximumBytes)
    {
        if (envelope is null || envelope.Version != PartitionMoveProtocol.Version
            || envelope.MoveId == Guid.Empty || envelope.Nonce == Guid.Empty
            || envelope.Partition is null || envelope.ControlOwner is null
            || envelope.SourcePlacement is null || envelope.DestinationOwner is null
            || envelope.SourcePlacement.Partition != envelope.Partition
            || envelope.ControlOwner.Incarnation == Guid.Empty
            || envelope.DestinationOwner.Incarnation == Guid.Empty
            || envelope.SourcePlacement.Incarnation == Guid.Empty
            || envelope.SourcePlacement.Incarnation == envelope.DestinationOwner.Incarnation
            || envelope.SourcePlacement.PhysicalShardId == envelope.DestinationOwner.PhysicalShardId
            || !Enum.IsDefined(envelope.Stage)
            || envelope.PageOrdinal < PartitionMoveProtocol.EmptyCount
            || !PartitionMoveSourceFenceValidation.ValidDigest(envelope.ControlIntentDigest)
            || envelope.Body.IsEmpty)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        if (envelope.Body.Length > maximumBytes || NativeSerialization.Measure(envelope) > maximumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, PartitionMoveProtocol.Capacity); }
    }

    internal static void RequireControl(PartitionMovePeerEnvelope envelope,
        PartitionMoveControlRecord control, DateTimeOffset now)
    {
        if (envelope.ExpiresAt <= now)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        if (envelope.MoveId != control.MoveId || envelope.Partition != control.Partition
            || envelope.ControlIntentDigest != PartitionMoveIntentIdentity.Digest(control)
            || !PartitionMoveControlValidation.SameSource(envelope.SourcePlacement, control.SourcePlacement)
            || !PhysicalOwnerEntryValidation.SameOwner(envelope.DestinationOwner, control.DestinationOwner)
            || control.Phase is PartitionMovePhase.Aborted or PartitionMovePhase.Retired)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }
}
