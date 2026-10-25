using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferRepairCurrent operations against the caller's database context.</summary>
public static class RemoteTransferRepairCurrent
{
    internal static void RequireCurrentRemoteTransferRepair(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, RemoteTransferIntentRecord record, RemoteTransferCoordinationHint hint, RemoteTransferRepairClaims witness)
    {
        var receipt = engine.InspectQueueTransferReceipt(view, principal.Id, record.Destination, record.Source, record.TransferId);
        if (witness.Stage == QueueTransferRepairStage.Accept && receipt is not null)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale);
        }

        if (witness.Stage == QueueTransferRepairStage.Complete && (receipt is null || witness.ReceiptDigest != RemoteTransferCoordinationIdentity.IntentDigest(receipt.ReceiptToken)))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale);
        }

        var request = new RemoteTransferCoordinationReadRequest(RemoteTransferRepairProtocol.ReadPurpose, record.Source, record.Destination, record.TransferId, record.IntentToken, hint.AcceptGeneration, witness.FailedCommandId, witness.Stage, hint.AcceptPolicyGeneration, hint.CompleteGeneration, witness.Stage == QueueTransferRepairStage.Complete ? receipt!.ReceiptToken : null);
        var current = engine.ReadRemoteTransferRepairFailure(view, principal, request);
        if (current.RepairWitness is null || current.SourceState is not null || current.FailureWitness is not null || current.TargetReceipt is not null)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale);
        }

        var fresh = engine.VerifyRetained<RemoteTransferRepairClaims>(current.RepairWitness);
        if (fresh.OutcomeDigest != witness.OutcomeDigest || fresh.CommandFingerprint != witness.CommandFingerprint || fresh.OriginalPolicyEpoch != witness.OriginalPolicyEpoch || fresh.CurrentPolicyEpoch != witness.CurrentPolicyEpoch || fresh.CurrentFieldHeaderDigest != witness.CurrentFieldHeaderDigest || fresh.ReceiptDigest != witness.ReceiptDigest)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale);
        }

        engine.ValidateCommitToken(view, witness.Stage == QueueTransferRepairStage.Accept ? record.Destination.Partition : record.Source.Partition, witness.OwnerCut, ErrorCode.TokenInvalidated, RemoteTransferRepairProtocol.Invalid);
    }
}
