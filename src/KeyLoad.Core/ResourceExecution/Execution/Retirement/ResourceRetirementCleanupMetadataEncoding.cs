using KeyLoad.Core.Features.BackupRestore.Execution;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ResourceRetirementCleanupMetadata EncodeCleanupMetadata(AtomicPartitionRosterTransaction roster,
        PrincipalRecord principal, ReplicatedOperation operation, ResourceRetirementCleanupPageBodyV1 body,
        ResourceRetirementPhaseReceiptV1 receipt, ResourceRetirementTableCleanupSelection selection,
        ResourceRetirementCleanupReadView work, byte[] outcomeKey)
    {
        var progress = selection.Progress;
        var checkpoint = new ResourceRetirementCleanupCheckpointV1(body.SealedState.Control, body.Receiver,
            body.Partition, body.Previous is null ? ResourceRetirementProtocol.First
                : checked(body.Previous.Ordinal + ResourceRetirementProtocol.First),
            progress.Next.FamilyOrdinal, progress.Next.AfterKey, !progress.HasMore, receipt)
        {
            ExaminedBytes = work.ExaminedBytes,
            ExaminedRecords = work.ExaminedRecords,
            OutcomeIdentity = new(operation.PrincipalId, principal.PolicyEpoch, CommandFingerprint(operation)),
            AttemptId = body.Attempt!.AttemptId
        };
        ResourceRetirementCleanupCheckpointValidation.Require(checkpoint, Limits);
        var phaseResult = new ResourceRetirementPhaseResultV1(receipt, null, null) { CleanupCheckpoint = checkpoint };
        var outcome = BuildStoredOutcome(CommandFingerprint(operation), principal.PolicyEpoch,
            Result(phaseResult), null, null, CommandOutcomePartitionScope.Global, null,
            transferFailure: null, remoteTransferAuthority: null);
        var rows = new List<StorageMutation>
        {
            new(ResourceRetirementCleanupStorage.Key(body.SealedState.Control, body.Receiver, body.Partition),
                NativeSerialization.Serialize(checkpoint)),
            new(outcomeKey, NativeSerialization.Serialize(outcome)),
            new(KeySpace.ClockBytes, NativeSerialization.Serialize(operation.EvaluatedAt))
        };
        if (roster.CleanupReplicationIndex > ResourceRetirementProtocol.Empty)
        {
            rows.Add(new(KeySpace.AppliedBytes, NativeSerialization.Serialize(roster.CleanupReplicationIndex)));
        }
        return new(checkpoint, rows);
    }
}
