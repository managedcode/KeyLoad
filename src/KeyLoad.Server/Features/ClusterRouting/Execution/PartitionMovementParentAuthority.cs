using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentAuthority
{

    internal static PartitionMoveParentHeader RequireHeader(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state)
    {
        var header = state.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var original = request with { Mode = PartitionMoveMode.Transfer };
        if (header.MoveId != request.MoveId || header.Partition != request.Partition
            || header.OperatorPrincipalId != principalId
            || !NativeSerialization.Serialize(header.OriginalTransferRequest).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(original))
            || header.OriginalSourceOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(header.ControlOwner, state.Directory.ControlOwner))
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        if (state.CurrentOperatorPrincipalId != principalId || state.CurrentOperatorPolicyEpoch <= PartitionMovementProtocol.UnissuedPolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority); }
        return header;
    }

    internal static PartitionMoveControlRecord RequireControl(PartitionMoveParentState state)
        => state.Control ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);

    internal static PartitionMovePhaseResult RequireObserved(PartitionMoveParentPhase original,
        PartitionMovePeerStage expected)
    {
        if (original.Stage != expected || original.OriginalResult is null
            || original.ObservationCheckpointReceipt is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        if (original.OriginalResult.Error is { } failure)
        { throw Errors.Fail(failure, original.OriginalResult.SafeDetail ?? PartitionMoveProtocol.MissingAuthority); }
        var actual = original.OriginalResult.Get<PartitionMovePhaseResult>();
        if (actual.Stage != expected || actual.MoveId != original.MoveId
            || actual.Journal.CommandId != original.OriginalPhaseCommandId
            || actual.Journal.AppliedPosition <= PartitionMovementProtocol.NoAppliedPosition
            || !PhysicalOwnerEntryValidation.SameOwner(actual.Journal.PhysicalOwner, original.OriginalReceiverOwner))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return actual;
    }

    internal static PartitionMoveResult? RequireTerminal(PartitionMoveParentState state)
    {
        if (state.Header?.TerminalResult is not { } terminal)
        { return null; }
        if (state.Pending is not null || state.Header.TerminalObservationReceipt is not { } observed
            || state.CurrentReadCut < observed.AppliedPosition
            || terminal.MoveId != state.Header.MoveId || terminal.Partition != state.Header.Partition
            || terminal.Phase is not (PartitionMovePhase.Retired or PartitionMovePhase.Aborted))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return terminal;
    }
}
