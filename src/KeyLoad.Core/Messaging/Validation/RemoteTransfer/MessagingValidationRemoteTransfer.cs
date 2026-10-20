using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationRemoteTransfer operations against the caller's database context.</summary>
public static class MessagingValidationRemoteTransfer
{
    internal static void RequireRemoteTransferAttemptHistory(this DatabaseEngine engine, RemoteTransferIntentRecord record, RemoteTransferIntentClaims original)
    {
        if (record.Attempts is not { } state)
        {
            return;
        }

        RemoteTransferAttemptStateValidation.Require(state);
        var intentDigest = RemoteTransferCoordinationIdentity.IntentDigest(record.IntentToken);
        foreach (var reference in state.History)
        {
            var witness = engine.Verify<RemoteTransferAcceptFailureClaims>(reference.WitnessToken, engine.Limits.MaxBatchBytes);
            var stamp = witness?.OriginalAuthority;
            if (witness is null || stamp?.Dependency is null || witness.TargetCut is null || witness.CurrentDependency is null || !RemoteTransferDependencyShape.Valid(stamp.Dependency) || !RemoteTransferDependencyShape.Valid(witness.CurrentDependency) || !RemoteTransferDependencyShape.Digest(witness.OutcomeDigest) || witness.ReadGeneration < RemoteTransferAttemptProtocol.NoUsage || witness.TargetCut.Position < RemoteTransferAttemptProtocol.RuntimeMinimumPosition || witness.Purpose != RemoteTransferAttemptProtocol.FailurePurpose || !Enum.IsDefined(witness.FailureKind) || stamp.Source != record.Source || stamp.Destination != record.Destination || stamp.TransferId != record.TransferId || stamp.PrincipalId != record.PrincipalId || stamp.MessageFingerprint != record.Fingerprint || stamp.IntentDigest != intentDigest || stamp.AcceptCommandId != reference.AcceptCommandId || witness.OutcomeDigest != reference.OutcomeDigest || stamp.TargetIncarnation != witness.TargetCut.Incarnation || witness.TargetCut.AtomicPartitionId != record.Destination.Partition.AtomicPartitionId || reference.AcceptCommandId != RemoteTransferAttemptIdentity.AcceptId(record.Source, record.Destination, record.TransferId, record.PrincipalId, record.Fingerprint, intentDigest, original.Incarnation, reference.Generation) || !RemoteTransferCapacityRepair.Improved(witness))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteTransferAttemptProtocol.Invalid);
            }
        }
    }
}
