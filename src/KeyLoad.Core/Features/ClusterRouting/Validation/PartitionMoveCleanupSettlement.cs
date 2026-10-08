using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PartitionMoveCleanupSettlement
{
    internal static PartitionMovePhaseGrant Require(IKeyValueView view, PartitionMoveControlRecord control,
        PartitionMovePeerStage stage, PartitionMoveCleanupRole role, Guid grantId,
        PartitionMoveJournalReceipt receipt, ReadOnlyMemory<byte> bodyBytes, int maximumBytes)
    {
        if (bodyBytes.IsEmpty || bodyBytes.Length > maximumBytes)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        var grant = PartitionMoveGrantStorage.Read(view, control.Partition, grantId, maximumBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(bodyBytes.Span);
        if (grant.MoveId != control.MoveId || grant.Stage != stage || grant.Partition != control.Partition
            || grant.OperatorPrincipalId != control.PrincipalId || grant.Settlement is null
            || grant.AbortDisposition is not null || grant.PhaseCommandId != receipt.CommandId
            || grant.BodyDigest != Convert.ToHexStringLower(SHA256.HashData(bodyBytes.Span))
            || JsonData.Fingerprint(grant.Settlement) != JsonData.Fingerprint(receipt)
            || body.Role != role || body.FamilyOrdinal != PartitionMoveCleanupFamilies.All.Length
            || body.OperatorPrincipalId != control.PrincipalId
            || JsonData.Fingerprint(body.Control) != JsonData.Fingerprint(control)
            || receipt.AppliedPosition <= PartitionMoveProtocol.EmptyCount)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return grant;
    }
}
