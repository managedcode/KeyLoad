using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementReceiver
{
    private async Task<GrainOperationReply> ApplyPhaseAsync(Guid commandId, PartitionMovePeerEnvelope verified,
        CancellationToken cancellationToken)
    {
        if (verified.Stage != PartitionMovePeerStage.Abort)
        { return await ApplyAsync(commandId, verified, cancellationToken).ConfigureAwait(false); }
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(verified.Body.Span);
        if (body.Role != PartitionMoveCleanupRole.Source)
        { return await ApplyAsync(commandId, verified, cancellationToken).ConfigureAwait(false); }
        var retained = source();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => retained.CloseMoveAsync(verified.Partition, verified.MoveId), failures).ConfigureAwait(false);
        if (failures.Any(error => !NativeCqrsBoundaryErrors.IsNonFatal(error)))
        { ServerFailureObserver.ThrowIfAny(failures); }
        GrainOperationReply? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            retained.RequireMoveJoined(verified.Partition, verified.MoveId);
            result = await ApplyAsync(commandId, verified, cancellationToken).ConfigureAwait(false);
            RequireAbortSettlement(result, commandId, verified);
            retained.ConfirmMoveClosed(verified.Partition, verified.MoveId);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
    }

    private void RequireAbortSettlement(GrainOperationReply reply, Guid commandId, PartitionMovePeerEnvelope original)
    {
        if (reply.Error is not null)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, PartitionMovementProtocol.Unavailable); }
        if (GrainNativePayload.Read<GrainValue>(reply.Payload).Value is not PartitionMovePhaseResult actual || actual.MoveId != original.MoveId || actual.Stage != PartitionMovePeerStage.Abort
            || actual.Journal.CommandId != commandId
            || actual.Journal.AppliedPosition <= PartitionMoveProtocol.EmptyCount
            || actual.Journal.ControlIntentDigest != original.ControlIntentDigest
            || !PhysicalOwnerEntryValidation.SameOwner(actual.Journal.PhysicalOwner, LocalOwner()))
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, PartitionMovementProtocol.Unavailable); }
    }
}
