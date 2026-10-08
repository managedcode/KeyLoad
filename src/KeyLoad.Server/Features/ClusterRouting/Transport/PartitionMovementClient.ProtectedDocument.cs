using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementClient
{
    internal async Task<PartitionMovementDispatchResult> DispatchProtectedDocumentAsync(Guid phaseCommandId,
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
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        partition.Database.ValidatePartitionMovementDispatch(original, authorization, phaseCommandId, work);
        var result = await DispatchAdmittedAsync(phaseCommandId, original, authorization,
            PartitionMovementPeerAction.Apply, Guid.Empty, default, null, cancellationToken).ConfigureAwait(false);
        work.Check();
        return result;
    }
}
