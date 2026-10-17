using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRetirementTopologyOpen(IKeyValueView view, ReplicatedOperation operation)
    {
        if (operation.Kind is OperationKind.RegisterPhysicalOwner or OperationKind.BootstrapPhysicalShardCatalog)
        {
            ResourceRetirementPreparationStorage.RequireOwnerRegistrationOpen(view, Limits);
            return;
        }
        if (operation.Kind is OperationKind.BindAtomicPartitionPlacement or OperationKind.ConfigurePlacementPolicy
            or OperationKind.PersistPlacementIntent or OperationKind.PlacementIntentPhase)
        {
            ResourceRetirementStorage.RequireNoRetirement(view, Limits);
            return;
        }
        if (operation.Kind != OperationKind.PartitionMovementPhase)
        {
            return;
        }
        var phase = PartitionMovementNativePayload.Read(operation);
        if (!IsControlledResourceMovement(phase))
        {
            ResourceRetirementPreparationStorage.RequireMovementAdmission(view, phase, OperationLimitsOptions);
        }
    }

    private static bool IsControlledResourceMovement(PartitionMovePhaseCommand phase)
    {
        if (phase.Stage is PartitionMovePeerStage.ControlAdmitCommand or PartitionMovePeerStage.ControlApplyCommand
            or PartitionMovePeerStage.ControlAcknowledgeCommand or PartitionMovePeerStage.ControlFinalizeCommand)
        {
            return true;
        }
        if (phase.Stage == PartitionMovePeerStage.ControlAuthorize)
        {
            var body = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(phase.Body.Span);
            return body.Phase.Stage == PartitionMovePeerStage.ControlApplyCommand;
        }
        if (phase.Stage == PartitionMovePeerStage.ReceiverIssue)
        {
            var body = NativeSerialization.Deserialize<PartitionMoveReceiverIssueBody>(phase.Body.Span);
            return body.OriginalEnvelope.Stage == PartitionMovePeerStage.ControlApplyCommand;
        }
        return false;
    }
}
