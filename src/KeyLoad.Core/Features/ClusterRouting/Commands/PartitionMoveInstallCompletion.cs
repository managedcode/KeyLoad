using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult CompleteMoveInstall(IAtomicTransaction transaction,
        Guid commandId, PartitionMovePhaseCommand phase, long position,
        PartitionMoveInstallBody body, PartitionMoveTargetStage stage, int totalPages)
    {
        if (stage.AcceptedPages != totalPages || stage.InstalledPages != totalPages || stage.Installed || stage.Published)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        PartitionMoveInstalledImageValidation.Require(transaction, stage, Store.Identity.Incarnation, Limits);
        var token = MoveInstallationToken(transaction, phase, position);
        if (token.Incarnation != phase.DestinationOwner.Incarnation
            || token.OwnershipEpoch != phase.DestinationOwner.PlacementEpoch)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        PartitionMoveTargetStorage.Write(transaction, PartitionMoveTargetStorage.Key(phase.Partition),
            stage with { Installed = true, InstalledToken = token }, Limits.MaxBatchBytes);
        var receipt = new CommitReceipt(commandId, token, [], Durability);
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, commandId, position,
            phase.ControlIntentDigest), body.Control, body.Fence, receipt, null);
    }
}
