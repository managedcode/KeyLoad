using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedPartitionMovementParentEffect(Guid commandId, string principalId,
        PartitionMovePeerEnvelope incoming, ReadExecutionBudget work)
    {
        movementCheckpointVerifier.RequireReceiverAdministrator(this, principalId, work);
        var ownedBytes = NativeSerialization.Serialize(incoming);
        RequireNativeBudget(ownedBytes.Length);
        var owned = NativeSerialization.Deserialize<PartitionMovePeerEnvelope>(ownedBytes);
        PartitionMovePeerEnvelopeValidation.RequireStructure(owned, Limits.MaxBatchBytes);
        if (owned.Grant is not { RequireReceiverIssuance: true }
            || owned.ReceiverIssuanceProof is null || owned.SourceDispatchWitness is null
            || PartitionMoveGrantValidation.IsLocalControl(owned.Stage))
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority); }
        var admission = VerifyMoveReceiverEffectProof(principalId, commandId, owned, ownedBytes, work);
        work.Check();
        return CreateMovementOperationCore(commandId, principalId, EvaluationClock.GetUtcNow(), owned, admission);
    }

    private PartitionMoveReceiverEffectAdmission VerifyMoveReceiverEffectProof(string principalId, Guid commandId,
        PartitionMovePeerEnvelope owned, ReadOnlyMemory<byte> ownedBytes, ReadExecutionBudget work)
    {
        movementCheckpointVerifier.RequireReceiverAdministrator(this, principalId, work);
        var grant = owned.Grant;
        var receiver = owned.ReceiverIssuanceProof;
        var source = owned.SourceDispatchWitness;
        if (grant is not { RequireReceiverIssuance: true } || receiver is null || source is null)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PartitionMoveProtocol.MissingAuthority); }
        var verified = movementCheckpointVerifier.VerifyReceiverEffect(this, principalId, commandId, ownedBytes, work);
        if (verified != Convert.ToHexStringLower(SHA256.HashData(ownedBytes.Span)))
        { throw Errors.Fail(ErrorCode.Unauthenticated, PartitionMoveProtocol.MissingAuthority); }
        return new PartitionMoveReceiverEffectAdmission(PartitionMoveProtocol.Version,
            ReadMoveReceiverFirstNonce(principalId, commandId, owned, work),
            owned.ExpiresAt, grant, receiver, source);
    }
}
