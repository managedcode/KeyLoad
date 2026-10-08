using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveGrantValidation
{
    internal static void Require(PartitionMovePeerEnvelope envelope, Guid commandId,
        PhysicalShardRecord receiver, DateTimeOffset now)
    {
        var grant = envelope.Grant;
        if (grant is null || grant.Version != PartitionMoveProtocol.Version
            || grant.GrantId == Guid.Empty || grant.PhaseCommandId != commandId
            || grant.MoveId != envelope.MoveId || grant.Partition != envelope.Partition
            || grant.Stage != envelope.Stage || grant.PageOrdinal != envelope.PageOrdinal || grant.ControlIntentDigest != envelope.ControlIntentDigest
            || grant.OperatorPolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || string.IsNullOrWhiteSpace(grant.OperatorPrincipalId)
            || grant.AdmissionPosition <= PartitionMoveProtocol.EmptyCount || grant.Settlement is not null || grant.AbortDisposition is not null
            || grant.ExpiresAt != envelope.ExpiresAt || grant.ExpiresAt <= now
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ControlOwner, envelope.ControlOwner)
            || !PhysicalOwnerEntryValidation.SameOwner(grant.ReceiverOwner, receiver)
            || grant.BodyDigest != Convert.ToHexStringLower(SHA256.HashData(envelope.Body.Span)))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

    internal static bool IsLocalControl(PartitionMovePeerStage stage)
        => stage is PartitionMovePeerStage.ControlPrepare or PartitionMovePeerStage.ControlAdvance
            or PartitionMovePeerStage.ControlFinalize or PartitionMovePeerStage.ControlAuthorize
            or PartitionMovePeerStage.ControlAcknowledge or PartitionMovePeerStage.ControlAcceptFence
            or PartitionMovePeerStage.ControlBeginAbort or PartitionMovePeerStage.ControlFinalizeAbort
            or PartitionMovePeerStage.ControlCompleteRetirement or PartitionMovePeerStage.ControlCancelGrants
            or PartitionMovePeerStage.ControlAdmitCommand or PartitionMovePeerStage.ControlAcknowledgeCommand
            or PartitionMovePeerStage.ControlFinalizeCommand;
}
