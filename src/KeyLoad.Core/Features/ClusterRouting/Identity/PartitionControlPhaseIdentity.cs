using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireControlledCommandPhaseIdentity(Guid phaseCommandId, PartitionMovePeerEnvelope envelope)
    {
        var identity = envelope.Stage switch
        {
            PartitionMovePeerStage.ControlAdmitCommand => NativeSerialization.Deserialize<PartitionControlAdmitBody>(envelope.Body.Span)
                .OriginalOperation.Id,
            PartitionMovePeerStage.ControlAcknowledgeCommand => NativeSerialization.Deserialize<PartitionControlAcknowledgeBody>(envelope.Body.Span)
                .Identity.CommandId,
            PartitionMovePeerStage.ControlFinalizeCommand => NativeSerialization.Deserialize<PartitionControlFinalizeBody>(envelope.Body.Span)
                .Identity.CommandId,
            PartitionMovePeerStage.ControlApplyCommand => NativeSerialization.Deserialize<PartitionControlApplyBody>(envelope.Body.Span)
                .Delegation.Identity.CommandId,
            _ => Guid.Empty
        };
        if (identity != Guid.Empty && identity == phaseCommandId)
        { throw Errors.Fail(ErrorCode.Validation, PartitionMoveProtocol.Invalid); }
    }
}
