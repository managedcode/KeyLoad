using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementClientProtectedDocumentOperations
{
    internal static async Task<PartitionMovementDispatchResult> DispatchProtectedDocumentAsync(PartitionMovementClient owner, Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (original.Stage is not (PartitionMovePeerStage.ControlAdmitCommand
            or PartitionMovePeerStage.ControlAcknowledgeCommand or PartitionMovePeerStage.ControlFinalizeCommand
            or PartitionMovePeerStage.ControlApplyCommand or PartitionMovePeerStage.ControlAuthorize
            or PartitionMovePeerStage.ControlAcknowledge))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMovementProtocol.Unavailable); }
        work.Check();
        await owner.partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        owner.partition.Database.ValidatePartitionMovementDispatch(original, authorization, phaseCommandId, work);
        var result = await PartitionMovementClientDispatchOperations.DispatchAdmittedAsync(owner, phaseCommandId, original, authorization,
            PartitionMovementPeerAction.Apply, Guid.Empty, default, null, Guid.Empty, cancellationToken).ConfigureAwait(false);
        work.Check();
        return result;
    }
}
