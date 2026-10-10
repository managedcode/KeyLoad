using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireRemoteTransferRepairWitness(RemoteTransferRepairClaims claims, RemoteTransferCoordinationHint hint)
    {
        if (claims is null || !Enum.IsDefined(claims.Stage) || claims.OwnerCut is null
            || claims.Purpose != RemoteTransferRepairProtocol.Purpose || claims.Source != hint.Source
            || claims.Destination != hint.Destination || claims.TransferId != hint.TransferId
            || claims.PrincipalId != hint.PrincipalId || claims.MessageFingerprint != hint.Fingerprint
            || claims.IntentDigest != hint.IntentDigest || claims.CapacityGeneration != hint.AcceptGeneration
            || claims.PolicyGeneration != hint.AcceptPolicyGeneration || claims.CompleteGeneration != hint.CompleteGeneration
            || claims.OriginalPolicyEpoch < RemoteTransferAttemptProtocol.NoUsage
            || claims.CurrentPolicyEpoch < RemoteTransferAttemptProtocol.MinimumPolicyEpoch
            || claims.OriginalPolicyEpoch >= claims.CurrentPolicyEpoch
            || claims.ReadGeneration != claims.OwnerCut.Position
            || claims.OwnerCut.Position < RemoteTransferAttemptProtocol.MinimumNativePosition
            || claims.OwnerCut.Incarnation != hint.SourceCut.Incarnation
            || claims.OwnerCut.AtomicPartitionId != (claims.Stage == QueueTransferRepairStage.Accept
                ? hint.Destination.Partition.AtomicPartitionId : hint.Source.Partition.AtomicPartitionId)
            || !RemoteTransferDependencyShape.Digest(claims.CommandFingerprint)
            || !RemoteTransferDependencyShape.Digest(claims.OutcomeDigest)
            || !RemoteTransferDependencyShape.Digest(claims.CurrentFieldHeaderDigest)
            || (claims.Stage == QueueTransferRepairStage.Accept ? claims.ReceiptDigest is not null
                : !RemoteTransferDependencyShape.Digest(claims.ReceiptDigest!))
            || claims.FailedCommandId != RemoteTransferRepairIdentity.CommandId(hint, claims.Stage))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferRepairProtocol.Invalid); }
    }
}
