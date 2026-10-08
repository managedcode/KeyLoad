using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMovePhaseResult ExecuteControlledDocumentPhase(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, PartitionMovePhaseCommand phase, long position)
    {
        if (phase.Stage == PartitionMovePeerStage.ControlApplyCommand)
        {
            var effect = ApplyControlledDocumentEffect(transaction, phase, operation.Id, position, operation.EvaluatedAt);
            var effectJournal = MoveJournalReceipt(transaction, operation.Id, position, phase.ControlIntentDigest)
                with
            { EffectDigest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(effect))) };
            return new(phase.MoveId, phase.Stage, effectJournal, null, null, null, null,
                ControlledEffect: effect);
        }
        var record = ExecuteControlledDocumentAuthority(transaction, principal, phase, position, operation.EvaluatedAt);
        if (record.Delegation!.MoveId != phase.MoveId || record.Identity.Partition != phase.Partition)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return new(phase.MoveId, phase.Stage, MoveJournalReceipt(transaction, operation.Id, position,
            phase.ControlIntentDigest), null, null, null, null, ControlledCommand: record);
    }

    private PartitionControlCommandRecord ExecuteControlledDocumentAuthority(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMovePhaseCommand phase, long position, DateTimeOffset now)
    {
        switch (phase.Stage)
        {
            case PartitionMovePeerStage.ControlAdmitCommand:
                var admit = NativeSerialization.Deserialize<PartitionControlAdmitBody>(phase.Body.Span);
                RequireControlOperator(admit.OperatorPrincipalId, principal);
                return AdmitControlledDocumentCommand(transaction, principal, admit, position, now);
            case PartitionMovePeerStage.ControlAcknowledgeCommand:
                var acknowledge = NativeSerialization.Deserialize<PartitionControlAcknowledgeBody>(phase.Body.Span);
                RequireControlOperator(acknowledge.OperatorPrincipalId, principal);
                return AcknowledgeControlledDocumentCommand(transaction, principal, acknowledge);
            case PartitionMovePeerStage.ControlFinalizeCommand:
                var finalize = NativeSerialization.Deserialize<PartitionControlFinalizeBody>(phase.Body.Span);
                RequireControlOperator(finalize.OperatorPrincipalId, principal);
                return FinalizeControlledDocumentCommand(transaction, principal, finalize);
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, PartitionMoveProtocol.Invalid);
        }
    }

    private static void RequireControlOperator(string expected, PrincipalRecord principal)
    {
        if (!principal.ClusterAdministrator || expected != principal.Id)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
    }
}
