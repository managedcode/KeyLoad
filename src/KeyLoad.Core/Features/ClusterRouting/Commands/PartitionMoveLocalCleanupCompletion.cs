using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveCleanupState CompleteMoveLocalCleanup(IAtomicTransaction transaction,
        PartitionMovePhaseCommand phase, PartitionMoveCleanupBody body,
        PartitionMoveCleanupState state, PartitionMoveJournalReceipt journal)
    {
        if (body.Role == PartitionMoveCleanupRole.Target)
        {
            var target = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(transaction,
                PartitionMoveTargetStorage.Key(phase.Partition), Limits.MaxBatchBytes);
            if (target is not null)
            {
                transaction.Delete(PartitionMoveTargetStorage.Key(phase.Partition));
            }
        }
        else if (phase.Stage == PartitionMovePeerStage.Abort)
        { transaction.Delete(PartitionMoveSourceFenceStorage.Key(phase.Partition)); }
        else
        {
            var publication = body.Publication
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
            InstallMovePublication(transaction, publication, body.Control.SourcePlacement);
            transaction.Delete(PartitionMoveTargetStorage.Key(phase.Partition));
        }
        // Retired source retains its fence: obsolete physical source never regains write admission.
        return state with { Completion = journal, NextBatch = checked(state.NextBatch + PartitionMoveProtocol.SequenceStep) };
    }
}
